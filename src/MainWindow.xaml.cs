using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace Iconic
{
    public partial class MainWindow : Window
    {
        private const string DragFormat = "Iconic.Item";

        private static readonly (string Name, string Hex)[] Palette =
        {
            ("Blue", "#0A84FF"), ("Purple", "#BF5AF2"), ("Pink", "#FF375F"),
            ("Orange", "#FF9F0A"), ("Green", "#30D158"), ("Teal", "#64D2FF"),
            ("Yellow", "#FFD60A"), ("Red", "#FF453A"), ("Graphite", "#8E8E93")
        };

        private readonly LibraryStore _store = new LibraryStore();
        private Library Lib => _store.Library;

        private bool _suppressHide;
        private bool _quitting;
        private AppItem _downItem;
        private AppGroup _downGroup;
        private const string GroupDragFormat = "Iconic.Group";
        private Point _downPos;

        public MainWindow()
        {
            InitializeComponent();
            _store.Load();

            Width = Math.Max(MinWidth, Lib.WindowWidth);
            Height = Math.Max(MinHeight, Lib.WindowHeight);
            GroupsList.ItemsSource = Lib.Groups;

            foreach (var group in Lib.Groups)
                foreach (var item in group.Items)
                    IconLoader.Enqueue(item);

            SourceInitialized += (_, _) => Glass.Apply(this);
            Deactivated += (_, _) => { if (!_suppressHide && !Lib.IsPinned && IsVisible) HidePanel(); };
            PreviewKeyDown += Window_PreviewKeyDown;
            PreviewTextInput += Window_PreviewTextInput;
            PreviewMouseDown += Window_PreviewMouseDown;

            UpdateCount();
            UpdatePinLook();
            Dispatcher.InvokeAsync(MigrateAndRepair, DispatcherPriority.ApplicationIdle);
        }

        // ------------------------------------------------------------------
        // Pin (stay open when clicking away)
        // ------------------------------------------------------------------

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            Lib.IsPinned = !Lib.IsPinned;
            UpdatePinLook();
            _store.Save();
        }

        private void UpdatePinLook()
        {
            bool pinned = Lib.IsPinned;
            PinGlyph.Text = pinned ? "\uE840" : "\uE718";
            PinButton.Background = new SolidColorBrush(pinned
                ? Color.FromArgb(0xD9, 0x0A, 0x84, 0xFF)
                : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            PinButton.ToolTip = pinned
                ? "Pinned: Iconic stays open. Click to unpin."
                : "Pin: keep Iconic open when you click away";
        }

        // ------------------------------------------------------------------
        // Show / hide
        // ------------------------------------------------------------------

        public void Prepare() => new WindowInteropHelper(this).EnsureHandle();

        public void ShowPanel()
        {
            PlaceWindow();
            Search.Text = "";

            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;   // nudge to the front
            Topmost = false;

            PlayIntro();
            Search.Focus();

            if (!Lib.DesktopOffered)
            {
                Lib.DesktopOffered = true;
                _store.Save();
                Dispatcher.InvokeAsync(OfferFirstRunTidy, DispatcherPriority.ApplicationIdle);
            }
        }

        private void HidePanel()
        {
            if (WindowState == WindowState.Normal)
            {
                Lib.WindowWidth = Width;
                Lib.WindowHeight = Height;
                Lib.WindowLeft = Left;
                Lib.WindowTop = Top;
                _store.Save();
            }
            BackupService.AutoBackup(Lib);
            Hide();
        }

        private void PlaceWindow()
        {
            if (Lib.WindowLeft is double left && Lib.WindowTop is double top &&
                left >= SystemParameters.VirtualScreenLeft &&
                top >= SystemParameters.VirtualScreenTop &&
                left + 120 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top + 60 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                Left = left;
                Top = top;
                return;
            }
            CenterOnScreen();
        }

        private void CenterOnScreen()
        {
            var area = SystemParameters.WorkArea;
            if (Width > area.Width) Width = area.Width - 40;
            if (Height > area.Height) Height = area.Height - 40;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }

        private void PlayIntro()
        {
            var ease = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(340);
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, duration) { EasingFunction = ease });
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, duration) { EasingFunction = ease });
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Closing (Alt+F4) just tucks the panel away; "Quit Iconic" really exits.
            if (_quitting) { base.OnClosing(e); return; }
            e.Cancel = true;
            HidePanel();
        }

        // ------------------------------------------------------------------
        // Keyboard & search
        // ------------------------------------------------------------------

        private bool IsEditingText => Keyboard.FocusedElement is TextBox tb && !ReferenceEquals(tb, Search);

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || IsEditingText) return;
            if (Search.Text.Length > 0) Search.Text = "";
            else HidePanel();
            e.Handled = true;
        }

        // Start typing anywhere to search.
        private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            Search.Focus();
            Search.Text += e.Text;
            Search.CaretIndex = Search.Text.Length;
            e.Handled = true;
        }

        // Clicking away from a rename box saves it.
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox tb && !ReferenceEquals(tb, Search) && !tb.IsMouseOver)
                Keyboard.Focus(Search);
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var first = Lib.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.IsShown);
            if (first != null && Search.Text.Trim().Length > 0) Launch(first);
            e.Handled = true;
        }

        private void ApplyFilter()
        {
            if (Lib == null || Search == null) return;
            var query = Search.Text.Trim();
            bool searching = query.Length > 0;
            foreach (var group in Lib.Groups)
            {
                int shown = 0;
                foreach (var item in group.Items)
                {
                    item.IsShown = !searching || (item.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (item.IsShown) shown++;
                }
                group.ForceOpen = searching;
                group.IsShown = !searching || shown > 0;
            }
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        // ------------------------------------------------------------------
        // Opening items
        // ------------------------------------------------------------------

        private void Launch(AppItem item)
        {
            if (!DesktopService.Exists(item.Path) && !TryRepair(item))
            {
                if (Ask("Can't find this item",
                        $"\"{item.Name}\" is no longer at:\n{item.Path}\n\nYou can point Iconic to where it is now.",
                        "Find it…", "Cancel"))
                    Relink(item);
                return;
            }
            var path = item.Path;
            try
            {
                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    WorkingDirectory = IOPath.GetDirectoryName(path) ?? ""
                });
                if (!Lib.IsPinned) HidePanel();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled a permission prompt — nothing to report.
            }
            catch (Exception ex)
            {
                Ask("Couldn't open", ex.Message, "OK", null);
            }
        }

        private static void Reveal(AppItem item)
        {
            try { Process.Start("explorer.exe", $"/select,\"{item.Path}\""); } catch { }
        }

        // ------------------------------------------------------------------
        // Tiles: click, drag, drop, right-click
        // ------------------------------------------------------------------

        private static T Data<T>(object sender) where T : class =>
            (sender as FrameworkElement)?.DataContext as T;

        private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = Data<AppItem>(sender);
            if (item == null || item.IsEditing) { _downItem = null; return; }
            _downItem = item;
            _downPos = e.GetPosition(this);
        }

        private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_downItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _downPos.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _downPos.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var item = _downItem;
            _downItem = null;
            var tile = (FrameworkElement)sender;
            tile.Opacity = 0.35;
            try { DragDrop.DoDragDrop(tile, new DataObject(DragFormat, item.Id), DragDropEffects.Move); }
            finally { tile.Opacity = 1; }
        }

        private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = Data<AppItem>(sender);
            if (item != null && ReferenceEquals(item, _downItem))
            {
                _downItem = null;
                Launch(item);
            }
        }

        private void Any_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DragFormat)) e.Effects = DragDropEffects.Move;
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Tile_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(GroupDragFormat)) return; // let the group card handle it
            Any_DragOver(sender, e);
        }

        private void Tile_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(GroupDragFormat)) return;
            var target = Data<AppItem>(sender);
            var group = target == null ? null : GroupOf(target);
            if (group == null) return;

            var tile = (FrameworkElement)sender;
            int index = group.Items.IndexOf(target);
            if (e.GetPosition(tile).X > tile.ActualWidth / 2) index++;
            HandleDrop(e, group, index);
            e.Handled = true;
        }

        private void Group_Drop(object sender, DragEventArgs e)
        {
            var group = Data<AppGroup>(sender);
            if (group == null) return;
            e.Handled = true;

            if (e.Data.GetDataPresent(GroupDragFormat))
            {
                var dragged = Lib.Groups.FirstOrDefault(g => g.Id == e.Data.GetData(GroupDragFormat) as string);
                bool below = group.DropHint == "Bottom";
                ClearDropHints();
                if (dragged == null || ReferenceEquals(dragged, group)) return;

                int old = Lib.Groups.IndexOf(dragged);
                int index = Lib.Groups.IndexOf(group) + (below ? 1 : 0);
                if (index > old) index--;
                if (index != old) Lib.Groups.Move(old, index);
                Commit();
                return;
            }
            HandleDrop(e, group, group.Items.Count);
        }

        private void Group_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(GroupDragFormat)) { Any_DragOver(sender, e); return; }

            var group = Data<AppGroup>(sender);
            var card = (FrameworkElement)sender;
            if (group != null)
            {
                var hint = e.GetPosition(card).Y < card.ActualHeight / 2 ? "Top" : "Bottom";
                foreach (var g in Lib.Groups) g.DropHint = ReferenceEquals(g, group) ? hint : null;
            }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Group_DragLeave(object sender, DragEventArgs e)
        {
            var group = Data<AppGroup>(sender);
            var card = sender as FrameworkElement;
            if (group == null || card == null) return;
            // Only clear when the pointer really left the card (not when moving onto a child).
            var pos = e.GetPosition(card);
            if (pos.X < 0 || pos.Y < 0 || pos.X > card.ActualWidth || pos.Y > card.ActualHeight)
                group.DropHint = null;
        }

        private void ClearDropHints()
        {
            foreach (var g in Lib.Groups) g.DropHint = null;
        }

        private void GroupHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var group = Data<AppGroup>(sender);
            _downGroup = group == null || group.IsEditing || e.ClickCount > 1 ? null : group;
            _downPos = e.GetPosition(this);
        }

        private void GroupHeader_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_downGroup == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _downPos.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _downPos.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var group = _downGroup;
            _downGroup = null;
            var container = GroupsList.ItemContainerGenerator.ContainerFromItem(group) as UIElement;
            if (container != null) container.Opacity = 0.4;
            try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(GroupDragFormat, group.Id), DragDropEffects.Move); }
            finally
            {
                if (container != null) container.Opacity = 1;
                ClearDropHints();
            }
        }

        private void HandleDrop(DragEventArgs e, AppGroup group, int index)
        {
            if (e.Data.GetDataPresent(DragFormat))
            {
                var item = FindItem(e.Data.GetData(DragFormat) as string);
                if (item != null) MoveItem(item, group, index);
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                    if (AddReference(file, group, index)) index++;
                Commit();
            }
        }

        private void Tile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = Data<AppItem>(sender);
            if (item == null) return;
            _downItem = null;

            var menu = new ContextMenu();
            menu.Items.Add(MakeItem("Open", () => Launch(item)));
            menu.Items.Add(MakeItem("Show in folder", () => Reveal(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Rename", () => BeginEdit(item)));
            menu.Items.Add(MakeItem("Change icon…", () => ChangeIcon(item)));
            if (!string.IsNullOrEmpty(item.CustomIcon))
                menu.Items.Add(MakeItem("Use original icon", () => ResetIcon(item)));

            var moveTo = new MenuItem { Header = "Move to group" };
            var current = GroupOf(item);
            foreach (var group in Lib.Groups)
            {
                var target = group;
                moveTo.Items.Add(MakeItem(group.Name, () => MoveItem(item, target, target.Items.Count),
                    !ReferenceEquals(group, current), group.Accent));
            }
            moveTo.Items.Add(new Separator());
            moveTo.Items.Add(MakeItem("New group", () =>
            {
                var created = CreateGroup();
                MoveItem(item, created, 0);
                BeginEdit(created);
            }));
            menu.Items.Add(moveTo);

            menu.Items.Add(new Separator());
            if (item.OriginalPath != null)
                menu.Items.Add(MakeItem("Put back on desktop", () => RemoveItem(item)));
            menu.Items.Add(MakeItem("Delete…", () => DeleteItem(item)));

            OpenMenu(menu, (UIElement)sender, PlacementMode.MousePoint);
            e.Handled = true;
        }

        // ------------------------------------------------------------------
        // Groups
        // ------------------------------------------------------------------

        private void GroupName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            var group = Data<AppGroup>(sender);
            if (group != null) BeginEdit(group);
            e.Handled = true;
        }

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            var group = Data<AppGroup>(sender);
            if (group == null) return;
            group.IsCollapsed = !group.IsCollapsed;
            Commit();
        }

        private void GroupHeader_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var group = Data<AppGroup>(sender);
            if (group == null) return;
            int index = Lib.Groups.IndexOf(group);

            var menu = new ContextMenu();
            menu.Items.Add(MakeItem("Rename", () => BeginEdit(group)));

            var color = new MenuItem { Header = "Color" };
            foreach (var (name, hex) in Palette)
            {
                var value = hex;
                color.Items.Add(MakeItem(name, () => { group.Accent = value; Commit(); }, true, hex));
            }
            menu.Items.Add(color);

            menu.Items.Add(MakeItem("Sort A to Z", () => SortGroup(group), group.Items.Count > 1));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Move up", () => { Lib.Groups.Move(index, index - 1); Commit(); }, index > 0));
            menu.Items.Add(MakeItem("Move down", () => { Lib.Groups.Move(index, index + 1); Commit(); }, index < Lib.Groups.Count - 1));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Delete group", () => DeleteGroup(group), Lib.Groups.Count > 1));

            OpenMenu(menu, (UIElement)sender, PlacementMode.MousePoint);
            e.Handled = true;
        }

        private void NewGroup_Click(object sender, RoutedEventArgs e)
        {
            Search.Text = "";
            var group = CreateGroup();
            BeginEdit(group);
            Dispatcher.InvokeAsync(() =>
            {
                if (GroupsList.ItemContainerGenerator.ContainerFromItem(group) is FrameworkElement container)
                    container.BringIntoView();
            }, DispatcherPriority.Loaded);
        }

        private AppGroup CreateGroup()
        {
            var group = new AppGroup
            {
                Name = "New Group",
                Accent = Palette[Lib.Groups.Count % Palette.Length].Hex
            };
            Lib.Groups.Add(group);
            Commit();
            return group;
        }

        private void SortGroup(AppGroup group)
        {
            var sorted = group.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (int target = 0; target < sorted.Count; target++)
            {
                int current = group.Items.IndexOf(sorted[target]);
                if (current != target) group.Items.Move(current, target);
            }
            Commit();
        }

        private void DeleteGroup(AppGroup group)
        {
            var destination = Lib.Groups.FirstOrDefault(g => !ReferenceEquals(g, group));
            if (destination == null) return;
            foreach (var item in group.Items.ToList()) destination.Items.Add(item);
            Lib.Groups.Remove(group);
            Commit();
        }

        // ------------------------------------------------------------------
        // Inline rename (items and groups)
        // ------------------------------------------------------------------

        private void BeginEdit(AppItem item)
        {
            item.EditOriginal = item.Name;
            item.IsEditing = true;
        }

        private void BeginEdit(AppGroup group)
        {
            group.EditOriginal = group.Name;
            group.IsEditing = true;
        }

        private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox box && e.NewValue is bool visible && visible)
            {
                box.Dispatcher.InvokeAsync(() =>
                {
                    box.Focus();
                    Keyboard.Focus(box);
                    box.SelectAll();
                }, DispatcherPriority.Input);
            }
        }

        private void EditBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { EndEdit(sender, true); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndEdit(sender, false); e.Handled = true; }
        }

        private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndEdit(sender, true);

        private void EndEdit(object sender, bool keep)
        {
            switch ((sender as FrameworkElement)?.DataContext)
            {
                case AppItem item when item.IsEditing:
                    if (!keep || string.IsNullOrWhiteSpace(item.Name)) item.Name = item.EditOriginal;
                    item.Name = item.Name.Trim();
                    item.IsEditing = false;
                    break;
                case AppGroup group when group.IsEditing:
                    if (!keep || string.IsNullOrWhiteSpace(group.Name)) group.Name = group.EditOriginal;
                    group.Name = group.Name.Trim();
                    group.IsEditing = false;
                    break;
                default:
                    return;
            }
            Commit();
            if (IsVisible) Keyboard.Focus(Search);
        }

        // ------------------------------------------------------------------
        // Items: icons, moving, removing
        // ------------------------------------------------------------------

        private void ChangeIcon(AppItem item)
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Choose an icon for {item.Name}",
                Filter = "Images and programs|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.exe;*.lnk|All files|*.*"
            };
            if (!RunFileDialog(dialog)) return;

            var saved = IconService.ImportCustomIcon(item.Id, dialog.FileName);
            if (saved == null)
            {
                Ask("Couldn't use that file", "Pick an image (PNG, JPG, ICO) or a program to borrow its icon.", "OK", null);
                return;
            }
            DeleteQuietly(item.CustomIcon);
            item.CustomIcon = saved;
            IconLoader.Enqueue(item);
            Commit();
        }

        private void ResetIcon(AppItem item)
        {
            DeleteQuietly(item.CustomIcon);
            item.CustomIcon = null;
            IconLoader.Enqueue(item);
            Commit();
        }

        private void MoveItem(AppItem item, AppGroup to, int index)
        {
            var from = GroupOf(item);
            if (from == null) return;
            int old = from.Items.IndexOf(item);

            if (ReferenceEquals(from, to))
            {
                if (index > old) index--;
                index = Math.Clamp(index, 0, to.Items.Count - 1);
                if (index != old) to.Items.Move(old, index);
            }
            else
            {
                from.Items.RemoveAt(old);
                to.Items.Insert(Math.Clamp(index, 0, to.Items.Count), item);
            }
            Commit();
        }

        private bool AddReference(string path, AppGroup group, int index)
        {
            if (!DesktopService.Exists(path)) return false;
            if (Lib.Groups.SelectMany(g => g.Items).Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                return false;

            var item = new AppItem { Name = DesktopService.DisplayName(path), Path = path };
            group.Items.Insert(Math.Clamp(index, 0, group.Items.Count), item);
            IconLoader.Enqueue(item);
            return true;
        }

        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Add apps or files to Iconic",
                Multiselect = true,
                DereferenceLinks = false,
                Filter = "All files|*.*"
            };
            if (!RunFileDialog(dialog)) return;
            var group = Lib.Groups[0];
            foreach (var file in dialog.FileNames) AddReference(file, group, group.Items.Count);
            Commit();
        }

        private void RemoveItem(AppItem item)
        {
            if (item.OriginalPath != null && DesktopService.Exists(item.Path))
            {
                try { DesktopService.MoveTo(item.Path, AppPaths.Desktop, IOPath.GetFileName(item.OriginalPath)); }
                catch (Exception ex)
                {
                    Ask("Couldn't put it back", ex.Message, "OK", null);
                    return;
                }
            }
            else if (DesktopService.IsUnder(item.Path, AppPaths.SharedDir))
            {
                DeleteQuietly(item.Path);
            }
            GroupOf(item)?.Items.Remove(item);
            DeleteQuietly(item.CustomIcon);
            Commit();
        }

        /// <summary>
        /// Files Iconic stores (moved off the desktop, or its own copies) go to the Recycle Bin.
        /// Icons that point at an app's real location are only removed from Iconic.
        /// </summary>
        private void DeleteItem(AppItem item)
        {
            bool owned = DesktopService.IsUnder(item.Path, AppPaths.StorageDir) && DesktopService.Exists(item.Path);
            bool isFolder = Directory.Exists(item.Path);

            string message = !owned
                ? "This removes the icon from Iconic. The app or file itself isn't touched."
                : isFolder
                    ? "This folder and everything in it will go to the Recycle Bin. You can restore it from there if you change your mind."
                    : "It'll go to the Recycle Bin, so you can get it back if you need to.";

            if (!Ask($"Delete \"{item.Name}\"?", message, "Delete", "Cancel")) return;

            if (owned && !RecycleBin.Send(item.Path))
            {
                Ask("Couldn't delete", $"\"{item.Name}\" may be open in another program. Close it and try again.", "OK", null);
                return;
            }
            GroupOf(item)?.Items.Remove(item);
            DeleteQuietly(item.CustomIcon);
            Commit();
        }

        // ------------------------------------------------------------------
        // Saving and restoring the layout
        // ------------------------------------------------------------------

        private void SaveLayout()
        {
            Directory.CreateDirectory(BackupService.BackupDir);
            var dialog = new SaveFileDialog
            {
                Title = "Save your Iconic layout",
                FileName = $"Iconic layout {DateTime.Now:yyyy-MM-dd}.iconic",
                DefaultExt = ".iconic",
                Filter = "Iconic layout|*.iconic",
                InitialDirectory = BackupService.BackupDir
            };
            if (!RunFileDialog(dialog)) return;
            try
            {
                BackupService.Export(Lib, dialog.FileName);
                Ask("Layout saved", "Your groups, labels, order and custom icons are saved. Use ••• → Restore layout… to bring them back.", "OK", null);
            }
            catch (Exception ex) { Ask("Couldn't save", ex.Message, "OK", null); }
        }

        private void RestoreLayout()
        {
            Directory.CreateDirectory(BackupService.BackupDir);
            var dialog = new OpenFileDialog
            {
                Title = "Restore an Iconic layout",
                Filter = "Iconic layout|*.iconic|All files|*.*",
                InitialDirectory = BackupService.BackupDir
            };
            if (!RunFileDialog(dialog)) return;

            BackupFile backup;
            try { backup = BackupService.Read(dialog.FileName); }
            catch { backup = null; }
            if (backup?.Library?.Groups == null || backup.Library.Groups.Count == 0)
            {
                Ask("Couldn't read that file", "It doesn't look like an Iconic layout.", "OK", null);
                return;
            }

            int icons = backup.Library.Groups.Sum(g => g.Items.Count);
            if (!Ask("Restore this layout?",
                     $"This replaces your current layout with {backup.Library.Groups.Count} groups and {icons} icons " +
                     $"saved {backup.Created:MMM d, yyyy 'at' h:mm tt}.\n\nYour current layout is backed up first.",
                     "Restore", "Cancel"))
                return;

            try { BackupService.Export(Lib, IOPath.Combine(BackupService.BackupDir, $"Before restore {DateTime.Now:yyyy-MM-dd HHmmss}.iconic")); }
            catch { }

            Directory.CreateDirectory(AppPaths.IconsDir);
            foreach (var item in backup.Library.Groups.SelectMany(g => g.Items))
            {
                item.CustomIcon = null;
                if (backup.Icons != null && backup.Icons.TryGetValue(item.Id, out var data))
                {
                    try
                    {
                        var path = IOPath.Combine(AppPaths.IconsDir, $"{item.Id}-{DateTime.Now.Ticks}.png");
                        File.WriteAllBytes(path, Convert.FromBase64String(data));
                        item.CustomIcon = path;
                    }
                    catch { }
                }
            }

            Lib.Groups.Clear();
            foreach (var group in backup.Library.Groups) Lib.Groups.Add(group);
            foreach (var item in Lib.Groups.SelectMany(g => g.Items)) IconLoader.Enqueue(item);
            Commit();
            MigrateAndRepair();
        }

        // ------------------------------------------------------------------
        // Keeping shortcuts working
        // ------------------------------------------------------------------

        /// <summary>Copies a shared-desktop file into Iconic Library; returns the path to use.</summary>
        private static string SafeCopy(string path)
        {
            if (!File.Exists(path) || !DesktopService.IsUnder(path, AppPaths.PublicDesktop)) return path;
            try { return DesktopService.CopyTo(path, AppPaths.SharedDir, IOPath.GetFileName(path)); }
            catch { return path; }
        }

        /// <summary>Runs quietly after launch: protects shared-desktop items and fixes broken ones.</summary>
        private void MigrateAndRepair()
        {
            bool changed = false;
            foreach (var item in Lib.Groups.SelectMany(g => g.Items).ToList())
            {
                if (!DesktopService.Exists(item.Path))
                {
                    if (TryRepair(item, save: false)) changed = true;
                    continue;
                }
                var copy = SafeCopy(item.Path);
                if (!string.Equals(copy, item.Path, StringComparison.OrdinalIgnoreCase))
                {
                    item.Path = copy;
                    changed = true;
                }
            }
            if (changed) Commit();
        }

        /// <summary>Finds a missing shortcut again (desktop or Start Menu) and reconnects it.</summary>
        private bool TryRepair(AppItem item, bool save = true)
        {
            var found = DesktopService.FindReplacement(item.Path, item.Name);
            if (found == null) return false;
            try
            {
                if (DesktopService.IsUnder(found, AppPaths.Desktop) && Lib.KeepDesktopTidy)
                {
                    item.Path = DesktopService.MoveTo(found, AppPaths.StorageDir, IOPath.GetFileName(found));
                    item.OriginalPath ??= found;
                }
                else if (DesktopService.IsUnder(found, AppPaths.Desktop))
                {
                    item.Path = found;
                }
                else
                {
                    item.Path = DesktopService.CopyTo(found, AppPaths.SharedDir, IOPath.GetFileName(found));
                }
            }
            catch { return false; }

            IconLoader.Enqueue(item);
            if (save) Commit();
            return true;
        }

        private void Relink(AppItem item)
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Where is {item.Name} now?",
                DereferenceLinks = false,
                Filter = "Programs and shortcuts|*.exe;*.lnk;*.url|All files|*.*"
            };
            if (!RunFileDialog(dialog)) return;
            item.Path = SafeCopy(dialog.FileName);
            IconLoader.Enqueue(item);
            Commit();
        }

        // ------------------------------------------------------------------
        // Desktop tidy-up
        // ------------------------------------------------------------------

        private void OfferFirstRunTidy()
        {
            // Shared desktop items (for all users) need admin rights to move,
            // so Iconic keeps its own copy and leaves the originals alone.
            var group = Lib.Groups[0];
            foreach (var path in DesktopService.FindPublicItems())
                AddReference(SafeCopy(path), group, group.Items.Count);
            Commit();

            var count = Candidates().Count;
            if (count == 0) return;

            if (Ask("Tidy up your desktop?",
                    $"Iconic can move the {count} item{(count == 1 ? "" : "s")} on your desktop into this window, " +
                    "leaving just the Iconic icon behind.\n\n" +
                    "They're kept in Documents\\Iconic Library, and you can put them all back anytime from the ••• menu.",
                    "Move them in", "Not now"))
            {
                TidyDesktop();
            }
        }

        private void TidyDesktop()
        {
            var candidates = Candidates();
            var group = Lib.Groups[0];
            var failed = new List<string>();

            foreach (var entry in candidates)
            {
                try
                {
                    var dest = DesktopService.MoveTo(entry, AppPaths.StorageDir, IOPath.GetFileName(entry));
                    var item = new AppItem { Name = DesktopService.DisplayName(entry), Path = dest, OriginalPath = entry };
                    group.Items.Add(item);
                    IconLoader.Enqueue(item);
                }
                catch { failed.Add(IOPath.GetFileName(entry)); }
            }

            Lib.KeepDesktopTidy = true;
            Commit();

            if (failed.Count > 0)
            {
                Ask("A few items stayed on the desktop",
                    "These couldn't be moved (they may be open or in use):\n\n" + string.Join("\n", failed.Take(12)),
                    "OK", null);
            }
        }

        private void RestoreDesktop()
        {
            var moved = Lib.Groups.SelectMany(g => g.Items).Where(i => i.OriginalPath != null).ToList();
            if (moved.Count == 0)
            {
                Ask("Nothing to put back", "Iconic hasn't moved anything off your desktop.", "OK", null);
                return;
            }
            if (!Ask("Put everything back on the desktop?",
                     $"{moved.Count} item{(moved.Count == 1 ? "" : "s")} will go back to your desktop. They'll stay listed in Iconic too.",
                     "Put back", "Cancel"))
                return;

            int failed = 0;
            foreach (var item in moved)
            {
                try
                {
                    if (DesktopService.Exists(item.Path))
                        item.Path = DesktopService.MoveTo(item.Path, AppPaths.Desktop, IOPath.GetFileName(item.OriginalPath));
                    item.OriginalPath = null;
                }
                catch { failed++; }
            }
            Lib.KeepDesktopTidy = false;
            Commit();
            if (failed > 0) Ask("Some items didn't move", $"{failed} item(s) couldn't be put back.", "OK", null);
        }

        private List<string> Candidates() =>
            DesktopService.FindCandidates()
                .Where(p => !Lib.IgnoredDesktopItems.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToList();


        // ------------------------------------------------------------------
        // ••• menu
        // ------------------------------------------------------------------

        private void More_Click(object sender, RoutedEventArgs e)
        {
            bool startup = StartupService.IsEnabled;
            var menu = new ContextMenu();
            menu.Items.Add(MakeItem("Add apps or files…", AddFiles));
            int newOnDesktop = Candidates().Count;
            menu.Items.Add(MakeItem(newOnDesktop > 0 ? $"Tidy up desktop ({newOnDesktop} new)" : "Tidy up desktop",
                TidyDesktop, newOnDesktop > 0));
            menu.Items.Add(MakeItem("Put everything back on desktop…", RestoreDesktop));
            menu.Items.Add(MakeItem("Open Iconic Library folder", () =>
            {
                Directory.CreateDirectory(AppPaths.StorageDir);
                try { Process.Start("explorer.exe", $"\"{AppPaths.StorageDir}\""); } catch { }
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Save layout…", SaveLayout));
            menu.Items.Add(MakeItem("Restore layout…", RestoreLayout));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem((startup ? "✓  " : "") + "Start with Windows", () =>
            {
                try { StartupService.Set(!startup); }
                catch (Exception ex) { Ask("Couldn't change that setting", ex.Message, "OK", null); }
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Quit Iconic", () => { _quitting = true; _store.Save(); Application.Current.Shutdown(); }));
            OpenMenu(menu, MoreButton, PlacementMode.Bottom);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static MenuItem MakeItem(string header, Action onClick, bool enabled = true, string dotColor = null)
        {
            object content = header;
            if (dotColor != null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                Brush fill;
                try { fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotColor)); }
                catch { fill = Brushes.Gray; }
                row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = fill, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
                content = row;
            }
            var menuItem = new MenuItem { Header = content, IsEnabled = enabled };
            menuItem.Click += (_, _) => onClick();
            return menuItem;
        }

        private static void OpenMenu(ContextMenu menu, UIElement target, PlacementMode placement)
        {
            menu.PlacementTarget = target;
            menu.Placement = placement;
            menu.IsOpen = true;
        }

        private bool Ask(string title, string message, string primary, string secondary)
        {
            _suppressHide = true;
            try
            {
                var dialog = new DialogWindow(title, message, primary, secondary);
                if (IsVisible)
                {
                    dialog.Owner = this;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                return dialog.ShowDialog() == true;
            }
            finally
            {
                _suppressHide = false;
                if (IsVisible) Activate();
            }
        }

        private bool RunFileDialog(FileDialog dialog)
        {
            _suppressHide = true;
            try { return dialog.ShowDialog(this) == true; }
            finally
            {
                _suppressHide = false;
                if (IsVisible) Activate();
            }
        }

        private AppGroup GroupOf(AppItem item) => Lib.Groups.FirstOrDefault(g => g.Items.Contains(item));

        private AppItem FindItem(string id) =>
            id == null ? null : Lib.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Id == id);

        private static void DeleteQuietly(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private void UpdateCount()
        {
            int total = Lib.Groups.Sum(g => g.Items.Count);
            CountText.Text = $"{total} item{(total == 1 ? "" : "s")}";
        }

        private void Commit()
        {
            ApplyFilter();
            UpdateCount();
            _store.Save();
        }
    }
}

﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using TEdit.MvvmLight.Threading;
using TEdit.Utility;
using Microsoft.Win32;
using TEditXna.Editor;
using TEditXna.Editor.Clipboard;
using TEditXna.Editor.Plugins;
using TEditXna.Editor.Tools;
using TEditXna.Editor.Undo;
using TEditXna.Properties;
using TEditXna.Render;
using TEditXNA.Terraria;
using TEditXNA.Terraria.Objects;
using TEditXna.View.Popups;
using DispatcherHelper = GalaSoft.MvvmLight.Threading.DispatcherHelper;

namespace TEditXna.ViewModel
{
    public partial class WorldViewModel : ViewModelBase
    {
        private readonly BrushSettings _brush = new BrushSettings();
        private readonly ClipboardManager _clipboard;
        private readonly Stopwatch _loadTimer = new Stopwatch();
        private readonly MouseTile _mouseOverTile = new MouseTile();
        private readonly ObservableCollection<IPlugin> _plugins = new ObservableCollection<IPlugin>();
        private readonly ObservableCollection<string> _points = new ObservableCollection<string>();
        private readonly Timer _saveTimer = new Timer();
        private readonly Selection _selection = new Selection();
        private readonly TilePicker _tilePicker = new TilePicker();
        private readonly ObservableCollection<ITool> _tools = new ObservableCollection<ITool>();
        private readonly UndoManager _undoManager;
        public bool[] CheckTiles;
        private ITool _activeTool;
        private bool _checkUpdates;
        private string _currentFile;
        private World _currentWorld;
        private bool _isAutoSaveEnabled = true;
        private ICommand _launchWikiCommand;
        private WriteableBitmap _minimapImage;
        private MorphBiome _morphBiomeTarget;
        private PixelMapManager _pixelMap;
        private ProgressChangedEventArgs _progress;
        private Chest _selectedChest;
        private Item _selectedChestItem;
        private string _selectedPoint;
        private Sign _selectedSign;
        private Sprite _selectedSprite;
        private int _selectedTabIndex;
        private bool _showGrid = true;
        private bool _showLiquid = true;
        private bool _showPoints = true;
        private bool _showTextures = true;
        private bool _showTiles = true;
        private bool _showWalls = true;
        private bool _showWires = true;
        private string _spriteFilter;
        private ListCollectionView _spritesView;
        private ICommand _viewLogCommand;
        private string _windowTitle;
        private ICommand _checkUpdatesCommand;


        public ICommand CheckUpdatesCommand
        {
            get { return _checkUpdatesCommand ?? (_checkUpdatesCommand = new RelayCommand<bool>(CheckVersion)); }
        }

        static WorldViewModel()
        {
            if (!Directory.Exists(TempPath))
            {
                Directory.CreateDirectory(TempPath);
            }
        }

        public WorldViewModel()
        {
            if (IsInDesignModeStatic)
                return;

            CheckUpdates = Properties.Settings.Default.CheckUpdates;

            if (CheckUpdates)
                CheckVersion();


            IsAutoSaveEnabled = Settings.Default.Autosave;

            _undoManager = new UndoManager(this);
            _clipboard = new ClipboardManager(this);
            World.ProgressChanged += OnProgressChanged;
            Brush.BrushChanged += OnPreviewChanged;
            UpdateTitle();

            _spriteFilter = string.Empty;
            _spritesView = (ListCollectionView)CollectionViewSource.GetDefaultView(World.Sprites);
            _spritesView.Filter = o =>
            {
                if (string.IsNullOrWhiteSpace(_spriteFilter)) return true;

                var sprite = (Sprite)o;

                if (sprite.TileName == _spriteFilter) return true;
                if (sprite.Name == _spriteFilter) return true;
                if (sprite.TileName != null && sprite.TileName.IndexOf(_spriteFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (sprite.Name != null && sprite.Name.IndexOf(_spriteFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;

                return false;
            };

            _saveTimer.AutoReset = true;
            _saveTimer.Elapsed += SaveTimerTick;
            // 3 minute save timer
            _saveTimer.Interval = 3 * 60 * 1000;

            _undoManager.Redid += UpdateMinimap;
            _undoManager.Undid += UpdateMinimap;
            _undoManager.UndoSaved += UpdateMinimap;

            // Test File Association and command line
            if (Application.Current.Properties["OpenFile"] != null)
            {
                string filename = Application.Current.Properties["OpenFile"].ToString();
                LoadWorld(filename);
            }
        }


        public WriteableBitmap MinimapImage
        {
            get { return _minimapImage; }
            set { Set("MinimapImage", ref _minimapImage, value); }
        }

        public ListCollectionView SpritesView
        {
            get { return _spritesView; }
            set { Set("SpritesView", ref _spritesView, value); }
        }

        public string SpriteFilter
        {
            get { return _spriteFilter; }
            set
            {
                Set("SpriteFilter", ref _spriteFilter, value);

                SpritesView.Refresh();
            }
        }


        public ICommand LaunchWikiCommand
        {
            get { return _launchWikiCommand ?? (_launchWikiCommand = new RelayCommand(() => Process.Start("http://github.com/BinaryConstruct/Terraria-Map-Editor/wiki"))); }
        }

        public static string TempPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TEdit"); }
        }

        public int SelectedTabIndex
        {
            get { return _selectedTabIndex; }
            set { Set("SelectedTabIndex", ref _selectedTabIndex, value); }
        }

        public Sign SelectedSign
        {
            get { return _selectedSign; }
            set
            {
                Set("SelectedSign", ref _selectedSign, value);
                SelectedTabIndex = 5;
            }
        }

        public Chest SelectedChest
        {
            get { return _selectedChest; }
            set
            {
                Set("SelectedChest", ref _selectedChest, value);
                SelectedTabIndex = 3;
            }
        }

        public ObservableCollection<IPlugin> Plugins
        {
            get { return _plugins; }
        }

        public MorphBiome MorphBiomeTarget
        {
            get { return _morphBiomeTarget; }
            set { Set("MorphBiomeTarget", ref _morphBiomeTarget, value); }
        }

        public bool IsAutoSaveEnabled
        {
            get { return _isAutoSaveEnabled; }
            set
            {
                Set("IsAutoSaveEnabled", ref _isAutoSaveEnabled, value);
                Settings.Default.Autosave = _isAutoSaveEnabled;
                Settings.Default.Save();
            }
        }

        public bool ShowGrid
        {
            get { return _showGrid; }
            set { Set("ShowGrid", ref _showGrid, value); }
        }

        public bool ShowTextures
        {
            get { return _showTextures; }
            set { Set("ShowTextures", ref _showTextures, value); }
        }


        public Sprite SelectedSprite
        {
            get { return _selectedSprite; }
            set
            {
                Set("SelectedSprite", ref _selectedSprite, value);
                PreviewChange();
            }
        }

        public ObservableCollection<string> Points
        {
            get { return _points; }
        }

        public string SelectedPoint
        {
            get { return _selectedPoint; }
            set { Set("SelectedPoint", ref _selectedPoint, value); }
        }


        public Item SelectedChestItem
        {
            get { return _selectedChestItem; }
            set { Set("SelectedChestItem", ref _selectedChestItem, value); }
        }

        public UndoManager UndoManager
        {
            get { return _undoManager; }
        }

        public ClipboardManager Clipboard
        {
            get { return _clipboard; }
        }

        public Selection Selection
        {
            get { return _selection; }
        }

        public MouseTile MouseOverTile
        {
            get { return _mouseOverTile; }
        }

        public string CurrentFile
        {
            get { return _currentFile; }
            set { Set("CurrentFile", ref _currentFile, value); }
        }

        public World CurrentWorld
        {
            get { return _currentWorld; }
            set { Set("CurrentWorld", ref _currentWorld, value); }
        }

        public ProgressChangedEventArgs Progress
        {
            get { return _progress; }
            set { Set("Progress", ref _progress, value); }
        }

        public string WindowTitle
        {
            get { return _windowTitle; }
            set { Set("WindowTitle", ref _windowTitle, value); }
        }

        public BrushSettings Brush
        {
            get { return _brush; }
        }

        public TilePicker TilePicker
        {
            get { return _tilePicker; }
        }

        public ObservableCollection<ITool> Tools
        {
            get { return _tools; }
        }

        public ITool ActiveTool
        {
            get { return _activeTool; }
            set { Set("ActiveTool", ref _activeTool, value); }
        }

        public PixelMapManager PixelMap
        {
            get { return _pixelMap; }
            set { Set("PixelMap", ref _pixelMap, value); }
        }

        public bool ShowWires
        {
            get { return _showWires; }
            set
            {
                Set("ShowWires", ref _showWires, value);
                UpdateRenderWorld();
            }
        }

        public bool ShowPoints
        {
            get { return _showPoints; }
            set { Set("ShowPoints", ref _showPoints, value); }
        }

        public bool ShowLiquid
        {
            get { return _showLiquid; }
            set
            {
                Set("ShowLiquid", ref _showLiquid, value);
                UpdateRenderWorld();
            }
        }

        public bool ShowTiles
        {
            get { return _showTiles; }
            set
            {
                Set("ShowTiles", ref _showTiles, value);
                UpdateRenderWorld();
            }
        }

        public bool ShowWalls
        {
            get { return _showWalls; }
            set
            {
                Set("ShowWalls", ref _showWalls, value);
                UpdateRenderWorld();
            }
        }


        public ICommand ViewLogCommand
        {
            get { return _viewLogCommand ?? (_viewLogCommand = new RelayCommand(ViewLog)); }
        }

        public bool CheckUpdates
        {
            get { return _checkUpdates; }
            set
            {
                Set("CheckUpdates", ref _checkUpdates, value);
                Settings.Default.CheckUpdates = value;
                Settings.Default.Save();
            }
        }

        private void UpdateMinimap(object sender, EventArgs eventArgs)
        {
            if (CurrentWorld != null)
            {
                if (MinimapImage != null)
                    RenderMiniMap.UpdateMinimap(CurrentWorld, ref _minimapImage);
            }
        }

        private void SaveTimerTick(object sender, ElapsedEventArgs e)
        {
            if (IsAutoSaveEnabled)
            {
                if (!string.IsNullOrWhiteSpace(CurrentFile))
                    SaveWorldThreaded(Path.Combine(TempPath, Path.GetFileNameWithoutExtension(CurrentFile) + ".autosave"));
                else
                    SaveWorldThreaded(Path.Combine(TempPath, "newworld.autosave"));
            }
        }

        private void ViewLog()
        {
            ErrorLogging.ViewLog();
        }

        private void UpdateTitle()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(asm.Location);

            WindowTitle = string.Format("TEdit v{0}.{1}.{2}.{3} {4}",
                fvi.ProductMajorPart, fvi.ProductMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart,
                Path.GetFileName(_currentFile));
        }

        public void CheckVersion(bool auto = true)
        {
            Task.Factory.StartNew<bool?>(() =>
            {
                try
                {
                    using (var Client = new WebClient())
                    {
                        byte[] dl = Client.DownloadData("http://www.binaryconstruct.com/downloads/teditversion.txt");

                        string vers = Encoding.UTF8.GetString(dl);
                        string[] verstrimmed = vers.Split('v');

                        if (verstrimmed.Length != 2) return null;

                        string[] split = verstrimmed[1].Split('.');

                        if (split.Length != 3) return null;

                        int major;
                        int minor;
                        int build;

                        if (!int.TryParse(split[0], out major)) return null;
                        if (!int.TryParse(split[1], out minor)) return null;
                        if (!int.TryParse(split[2], out build)) return null;

                        if (major > App.Version.ProductMajorPart) return true;
                        if (minor > App.Version.ProductMinorPart) return true;
                        if (build > App.Version.ProductBuildPart) return true;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
                return false;
            }).ContinueWith(t =>
            {
                bool? isoutofdate = t.Result;

                if (isoutofdate == null)
                {
                    MessageBox.Show("Unable to check version.", "Update Check Failed");
                }
                else if (isoutofdate == true)
                {
                    if (MessageBox.Show("You are using an outdated version of TEdit. Do you wish to download the update?", "Update?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        Process.Start("http://www.binaryconstruct.com/games/tedit");
                    }
                }
                else
                {
                    if (!auto)
                        MessageBox.Show("TEdit is up to date.", "Update");
                }

            }, TaskFactoryHelper.UiTaskScheduler);
        }

        public event EventHandler PreviewChanged;

        public void PreviewChange()
        {
            OnPreviewChanged(this, new EventArgs());
        }

        protected virtual void OnPreviewChanged(object sender, EventArgs e)
        {
            if (PreviewChanged != null) PreviewChanged(sender, e);
        }

        private void SetActiveTool(ITool tool)
        {
            if (ActiveTool != tool)
            {
                if (tool.Name == "Paste" && !CanPaste())
                    return;

                if (ActiveTool != null)
                    ActiveTool.IsActive = false;

                ActiveTool = tool;
                tool.IsActive = true;

                PreviewChange();
            }
        }

        private void OnProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            DispatcherHelper.CheckBeginInvokeOnUI(() => Progress = e);
        }

        public void MouseDownTile(TileMouseState e)
        {
            if (e.Location != MouseOverTile.MouseState.Location)
                MouseOverTile.Tile = CurrentWorld.Tiles[e.Location.X, e.Location.Y];

            MouseOverTile.MouseState = e;
            ActiveTool.MouseDown(e);

            CommandManager.InvalidateRequerySuggested();
        }

        public void MouseUpTile(TileMouseState e)
        {
            if (e.Location != MouseOverTile.MouseState.Location)
                MouseOverTile.Tile = CurrentWorld.Tiles[e.Location.X, e.Location.Y];

            MouseOverTile.MouseState = e;
            ActiveTool.MouseUp(e);
            CommandManager.InvalidateRequerySuggested();
        }

        public void MouseMoveTile(TileMouseState e)
        {
            if (e.Location.X >= 0 && e.Location.Y >= 0 && e.Location.X < CurrentWorld.TilesWide && e.Location.Y < CurrentWorld.TilesHigh)
            {
                if (e.Location != MouseOverTile.MouseState.Location)
                    MouseOverTile.Tile = CurrentWorld.Tiles[e.Location.X, e.Location.Y];

                MouseOverTile.MouseState = e;

                ActiveTool.MouseMove(e);
            }
        }

        private void OpenWorld()
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "Terraria World File|*.wld|Terraria World Backup|*.bak|TEdit Backup File|*.TEdit";
            ofd.DefaultExt = "Terraria World File|*.wld";
            ofd.Title = "Load Terraria World File";
            ofd.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Terraria\Worlds");
            ofd.Multiselect = false;
            if ((bool)ofd.ShowDialog())
            {
                LoadWorld(ofd.FileName);
            }
        }

        private void NewWorld()
        {
            var nwDialog = new NewWorldView();
            if ((bool)nwDialog.ShowDialog())
            {
                _loadTimer.Reset();
                _loadTimer.Start();
                _saveTimer.Stop();
                Task.Factory.StartNew(() =>
                {
                    World w = nwDialog.NewWorld;
                    w.SpawnX = w.TilesWide / 2;
                    w.SpawnY = (int)Math.Max(0, w.GroundLevel - 10);
                    w.GroundLevel = (int)w.GroundLevel;
                    w.RockLevel = (int)w.RockLevel;
                    w.BottomWorld = w.TilesHigh * 16;
                    w.RightWorld = w.TilesWide * 16;
                    w.Tiles = new Tile[w.TilesWide, w.TilesHigh];
                    var cloneTile = new Tile();
                    for (int y = 0; y < w.TilesHigh; y++)
                    {
                        OnProgressChanged(w, new ProgressChangedEventArgs(Calc.ProgressPercentage(y, w.TilesHigh), "Generating World..."));

                        if (y == (int)w.GroundLevel - 10)
                            cloneTile = new Tile { WireRed = false, IsActive = true, LiquidType = LiquidType.None, LiquidAmount = 0, Type = 2, U = -1, V = -1, Wall = 2 };
                        if (y == (int)w.GroundLevel - 9)
                            cloneTile = new Tile { WireRed = false, IsActive = true, LiquidType = LiquidType.None, LiquidAmount = 0, Type = 0, U = -1, V = -1, Wall = 2 };
                        else if (y == (int)w.GroundLevel + 1)
                            cloneTile = new Tile { WireRed = false, IsActive = true, LiquidType = LiquidType.None, LiquidAmount = 0, Type = 0, U = -1, V = -1, Wall = 0 };
                        else if (y == (int)w.RockLevel)
                            cloneTile = new Tile { WireRed = false, IsActive = true, LiquidType = LiquidType.None, LiquidAmount = 0, Type = 1, U = -1, V = -1, Wall = 0 };
                        else if (y == w.TilesHigh - 182)
                            cloneTile = new Tile();
                        for (int x = 0; x < w.TilesWide; x++)
                        {
                            w.Tiles[x, y] = (Tile)cloneTile.Clone();
                        }
                    }
                    return w;
                })
                    .ContinueWith(t => CurrentWorld = t.Result, TaskFactoryHelper.UiTaskScheduler)
                    .ContinueWith(t => RenderEntireWorld())
                    .ContinueWith(t =>
                    {
                        CurrentFile = null;
                        PixelMap = t.Result;
                        UpdateTitle();
                        Points.Clear();
                        Points.Add("Spawn");
                        Points.Add("Dungeon");
                        foreach (NPC npc in CurrentWorld.NPCs)
                        {
                            Points.Add(npc.Name);
                        }
                        MinimapImage = RenderMiniMap.Render(CurrentWorld);
                        _loadTimer.Stop();
                        OnProgressChanged(this, new ProgressChangedEventArgs(0, string.Format("World loaded in {0} seconds.", _loadTimer.Elapsed.TotalSeconds)));
                        _saveTimer.Start();
                    }, TaskFactoryHelper.UiTaskScheduler);
            }
        }

        private void SaveWorld()
        {
            if (CurrentWorld == null)
                return;

            if (string.IsNullOrWhiteSpace(CurrentFile))
                SaveWorldAs();
            else
                SaveWorldFile();
        }

        private void SaveWorldAs()
        {
            var sfd = new SaveFileDialog();
            sfd.Filter = "Terraria World File|*.wld|TEdit Backup File|*.TEdit";
            sfd.Title = "Save World As";
            sfd.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Terraria\Worlds");
            if ((bool)sfd.ShowDialog())
            {
                CurrentFile = sfd.FileName;
                SaveWorldFile();
            }
        }

        private void SaveWorldFile()
        {
            if (CurrentWorld.LastSave < File.GetLastWriteTimeUtc(CurrentFile))
            {
                MessageBoxResult overwrite = MessageBox.Show(_currentWorld.Title + " was externally modified since your last save.\r\nDo you wish to overwrite?", "World Modified", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (overwrite.Equals(MessageBoxResult.Cancel))
                    return;
            }
            SaveWorldThreaded(CurrentFile);
        }

        private void SaveWorldThreaded(string filename)
        {
            Task.Factory.StartNew(() => World.Save(CurrentWorld, filename))
                .ContinueWith(t => CommandManager.InvalidateRequerySuggested(), TaskFactoryHelper.UiTaskScheduler);
        }

        public void LoadWorld(string filename)
        {
            _loadTimer.Reset();
            _loadTimer.Start();
            _saveTimer.Stop();
            CurrentFile = filename;
            CurrentWorld = null;
            GC.WaitForFullGCComplete();

            Task.Factory.StartNew(() => World.LoadWorld(filename))
                .ContinueWith(t => CurrentWorld = t.Result, TaskFactoryHelper.UiTaskScheduler)
                .ContinueWith(t => RenderEntireWorld())
                .ContinueWith(t =>
                {
                    if (CurrentWorld != null)
                    {
                        PixelMap = t.Result;
                        UpdateTitle();
                        Points.Clear();
                        Points.Add("Spawn");
                        Points.Add("Dungeon");
                        foreach (NPC npc in CurrentWorld.NPCs)
                        {
                            Points.Add(npc.Name);
                        }
                        MinimapImage = RenderMiniMap.Render(CurrentWorld);
                        _loadTimer.Stop();
                        OnProgressChanged(this, new ProgressChangedEventArgs(0, string.Format("World loaded in {0} seconds.", _loadTimer.Elapsed.TotalSeconds)));
                        _saveTimer.Start();
                    }
                    _loadTimer.Stop();
                }, TaskFactoryHelper.UiTaskScheduler);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Kawazu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using Vanara.PInvoke;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;
using Color = Microsoft.Xna.Framework.Color;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace OverlayLock
{
    public class OverlayLockGame : Game
    {
        private HWND _targetHwnd = Process.GetProcessesByName("Discord").First().MainWindowHandle;
        private TimeSpan _afkTime = TimeSpan.FromSeconds(2);

        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private Texture2D _texture;
        
        private static User32.WinEventProc _targetMovedProc;
        private readonly Form _gameForm;
        private bool _dragging;
        private Vector2 _dragOffsets;
        private bool _resizing;
        NotifyIcon _notifyIcon = new ();
        public RECT TargetRect { get; set; }
        public RECT LastRenderedRect { get; set; }
        public bool _locked = false;
        public DateTime _lastActive = DateTime.MinValue;
        private SpriteFont _font;
        private Dictionary<string, string>? _currentPhraseEntry;
        private string _currentAttemptRomaji = "";
        private IntPtr _hwnd;
        private KawazuConverter _kawazu;
        private string _currentPhraseRomaji = "";
        private List<Dictionary<string, string>> _wordList;

        private string GetCurrentPhraseRomaji()
        {
            // deadlock
            var romaji = Task.Factory.StartNew(() => _kawazu.Convert(_currentPhraseEntry?["gana"] ?? "", To.Romaji).Result).Result;
            var bytes = Encoding.GetEncoding("ISO-8859-8").GetBytes(romaji);
            var romajiNormalized = Encoding.UTF8.GetString(bytes);
            return romajiNormalized;
        }

        public OverlayLockGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            // anti aliasing
            _graphics.PreferMultiSampling = true;

            IsMouseVisible = true;
            Window.IsBorderless = true;
            
            // fps
            IsFixedTimeStep = false;
            InactiveSleepTime = TimeSpan.FromSeconds(0.015);
            TargetElapsedTime = TimeSpan.FromSeconds(1d / 60);
            
            // render window
            var gameForm = (Form)Control.FromHandle(Window.Handle);
            gameForm.FormBorderStyle = FormBorderStyle.None;
            gameForm.WindowState = FormWindowState.Maximized;
            gameForm.ShowInTaskbar = false;
            _gameForm = gameForm;
            Utils.MakeFullScreenOverlay(Window.Handle, true);

            _graphics.ApplyChanges();
            
            _targetMovedProc = TargetMoved;
            var threadId = User32.GetWindowThreadProcessId(_targetHwnd, out var processId);
            User32.SetWinEventHook(
                User32.EventConstants.EVENT_SYSTEM_FOREGROUND,
                User32.EventConstants.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                _targetMovedProc,
                processId,
                threadId,
                User32.WINEVENT.WINEVENT_INCONTEXT | User32.WINEVENT.WINEVENT_SKIPOWNPROCESS);
            
            // game logic
            gameForm.MouseDoubleClick += (object? sender, MouseEventArgs args) =>
            {
                if ((args.Button & MouseButtons.Left) != 0)
                {
                    var windowplacement = new User32.WINDOWPLACEMENT();
                    User32.GetWindowPlacement(_targetHwnd, ref windowplacement);
                    if (windowplacement.showCmd == ShowWindowCommand.SW_NORMAL)
                    {
                        User32.ShowWindow(_targetHwnd, ShowWindowCommand.SW_SHOWMAXIMIZED);
                    }
                    else if (windowplacement.showCmd == ShowWindowCommand.SW_MAXIMIZE)
                    {
                        User32.ShowWindow(_targetHwnd, ShowWindowCommand.SW_SHOWNORMAL);
                    }
                }
            };
            Window.TextInput += (object? sender, TextInputEventArgs args) =>
            {
                _lastActive = DateTime.UtcNow;
                if (args.Key == Keys.Back)
                {
                    _currentAttemptRomaji = string.Join("", _currentAttemptRomaji.SkipLast(1));
                    return;
                }
                var chr = args.Character;
                if (_font?.Glyphs.ContainsKey(chr) == true)
                {
                    _currentAttemptRomaji += chr;
                    _currentPhraseRomaji = GetCurrentPhraseRomaji();
                    if (_currentPhraseRomaji == _currentAttemptRomaji)
                    {
                        _gameForm.Hide();
                        _locked = false;
                    }
                }
            };
        }

        public void TargetMoved(User32.HWINEVENTHOOK hwineventhook, uint eventType, HWND hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            switch (eventType)
            {
                case User32.EventConstants.EVENT_SYSTEM_MOVESIZESTART:
                case User32.EventConstants.EVENT_SYSTEM_MOVESIZEEND:
                case User32.EventConstants.EVENT_OBJECT_LOCATIONCHANGE:
                case User32.EventConstants.EVENT_SYSTEM_FOREGROUND:
                {
                    User32.GetWindowRect(_targetHwnd, out var targetRect);
                    TargetRect = targetRect;

                    if (eventType == User32.EventConstants.EVENT_SYSTEM_MOVESIZESTART)
                    {
                        _resizing = true;
                    }
                    if (eventType == User32.EventConstants.EVENT_SYSTEM_MOVESIZEEND)
                    {
                        _resizing = false;
                    }
                    if (eventType == User32.EventConstants.EVENT_SYSTEM_FOREGROUND && _locked && !_dragging)
                    {
                        User32.SetForegroundWindow(_hwnd);
                    }

                    _lastActive = DateTime.UtcNow;
                    break;
                }
            }
        }

        protected override void Initialize()
        {
            _notifyIcon.Icon = SystemIcons.Hand;
            _notifyIcon.Text = nameof(OverlayLock);
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(5000, $"{nameof(OverlayLock)}", $"{nameof(OverlayLock)} is now running",  ToolTipIcon.Info);
            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            var quitBtn = new ToolStripMenuItem("Exit");
            _notifyIcon.ContextMenuStrip.Items.Add(quitBtn);
            quitBtn.Click += (sender, args) =>
            {
                _notifyIcon.Visible = false;
                Exit();
            };

            _kawazu = new KawazuConverter();
            _hwnd = Window.Handle;
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _texture = new Texture2D(_spriteBatch.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
            _texture.SetData(new []
            {
                Color.White
            });
            _font = Content.Load<SpriteFont>("MS Gothic");
            _wordList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(File.ReadAllText("word_list.json")) ?? throw new InvalidOperationException();
            _wordList = _wordList.Where(e => e["word"].All(chr => _font.Glyphs.ContainsKey(chr))).ToList();
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            if (DateTime.UtcNow - _lastActive > _afkTime && !_locked)
            {
                User32.GetWindowRect(_targetHwnd, out var targetRect);
                TargetRect = targetRect;

                _currentAttemptRomaji = "";
                _currentPhraseEntry = _wordList.ElementAt(new Random().Next(0, _wordList.Count - 1));
                _currentPhraseRomaji = GetCurrentPhraseRomaji();
                _locked = true;
                _gameForm.Show();
            }

            if (_locked)
            {
                var mouse = Mouse.GetState();
                var mouseGlobal = Cursor.Position;
                if (mouseGlobal.X > TargetRect.left
                    && mouseGlobal.X < TargetRect.right
                    && mouseGlobal.Y > TargetRect.top
                    && mouseGlobal.Y < TargetRect.bottom
                    && mouse.LeftButton == ButtonState.Pressed
                    && !_dragging
                    && !_resizing)
                {
                    _dragging = true;
                    _dragOffsets = new Vector2(TargetRect.X - mouseGlobal.X, TargetRect.Y - mouseGlobal.Y);
                }
                if (_dragging && (mouse.LeftButton != ButtonState.Pressed || _resizing))
                {
                    _dragging = false;
                    _dragOffsets = Vector2.Zero;
                }
                if (_dragging && !_resizing)
                {
                    User32.GetWindowRect(_targetHwnd, out var targetRect);
                    User32.MoveWindow(_targetHwnd,
                        (int)(mouseGlobal.X + _dragOffsets.X),
                        (int)(mouseGlobal.Y + _dragOffsets.Y),
                        targetRect.Width,
                        targetRect.Height,
                        true);
                }
                
                if (!_targetHwnd.IsNull)
                {
                    var renderRect = new RECT
                    {
                        X = (LastRenderedRect.X + TargetRect.X) / 2,
                        Y = (LastRenderedRect.Y + TargetRect.Y) / 2,
                        Width = (LastRenderedRect.Width + TargetRect.Width) / 2,
                        Height = (LastRenderedRect.Height + TargetRect.Height) / 2
                    };
                    User32.MoveWindow(Window.Handle, renderRect.X, renderRect.Y, renderRect.Width, renderRect.Height, true);
                    LastRenderedRect = renderRect;
                    
                    var insertAfter = User32.GetWindow(_targetHwnd, User32.GetWindowCmd.GW_HWNDPREV);
                    if (User32.GetWindow(insertAfter, User32.GetWindowCmd.GW_HWNDPREV) != Window.Handle)
                    {
                        User32.SetWindowPos(Window.Handle, insertAfter, 0, 0, 0, 0, 0
                            | User32.SetWindowPosFlags.SWP_NOMOVE
                            | User32.SetWindowPosFlags.SWP_NOSIZE
                            | 0);
                    }
                }
            }
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Transparent);

            if (_locked)
            {
                _spriteBatch.Begin();
                _spriteBatch.Draw(_texture, new Rectangle(0, 0, Window.ClientBounds.Width, Window.ClientBounds.Height), new Color(222,222,255,244));
                if (_currentPhraseEntry != null)
                {
                    var word = _currentPhraseEntry["word"];
                    var gana = _currentPhraseEntry["gana"];
                    var meaning = _currentPhraseEntry["meaning"];
                    _spriteBatch.DrawString(_font, $"{word}{(word != gana ? $" [{gana}]" : "")} - {meaning}", new Vector2(50, 50), Color.MediumPurple, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
                }

                if (!string.IsNullOrEmpty(_currentAttemptRomaji))
                {
                    _spriteBatch.DrawString(_font, _currentAttemptRomaji, new Vector2(50, 120), Color.Orange, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
                    if (!string.IsNullOrEmpty(_currentPhraseRomaji))
                    {
                        var currentCorrectAttempt = string.Join("", _currentAttemptRomaji
                                .Zip(_currentPhraseRomaji)
                                .ToList()
                                .TakeWhile(pair => pair.First == pair.Second)
                                .Select(pair => pair.First));
                        _spriteBatch.DrawString(_font, currentCorrectAttempt, new Vector2(50, 120), Color.OrangeRed, 0, new Vector2(0, 0), 1.0f, SpriteEffects.None, 1);
                    }
                }
                _spriteBatch.End();
            }

            base.Draw(gameTime);
        }
    }
}

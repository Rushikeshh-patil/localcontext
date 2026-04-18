using System;
using System.Windows;
using System.Windows.Threading;

namespace LocalContextBuilder
{
    public partial class MainWindow : Window
    {
        private LlmService _llmService;
        private KeyboardHook _keyboardHook;
        private string _currentSuggestion = "";

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void Log(string msg)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " - " + msg + "\n");
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("App Loaded");
            _llmService = new LlmService();
            _llmService.StartServer(@"C:\Users\rush\.ollama\models\gemma-4-E4B-it-UD-Q8_K_XL.gguf");
            Log("LLM Service Started");

            _keyboardHook = new KeyboardHook();
            _keyboardHook.OnPauseTyping += KeyboardHook_OnPauseTyping;
            _keyboardHook.OnTyping += KeyboardHook_OnTyping;
            _keyboardHook.OnAcceptSuggestion += KeyboardHook_OnAcceptSuggestion;
            _keyboardHook.Start();
            Log("Keyboard Hook Started");
        }

        private int _requestCounter = 0;

        private void KeyboardHook_OnTyping()
        {
            Dispatcher.Invoke(() =>
            {
                _requestCounter++; // Invalidate any pending LLM requests
                if (MainBorder.Visibility != Visibility.Hidden)
                {
                    MainBorder.Visibility = Visibility.Hidden;
                    _currentSuggestion = "";
                }
            });
        }

        private async void KeyboardHook_OnPauseTyping(string context)
        {
            int currentRequest = _requestCounter;
            Log($"Pause typed: '{context}' (Length: {context?.Length})");
            if (string.IsNullOrWhiteSpace(context) || context.Length < 3) return;

            string screenContext = await ScreenReader.GetActiveWindowTextAsync();
            Log($"Screen Context extracted: {screenContext?.Length} characters");
            
            string suggestion = await _llmService.GenerateSuggestion(context, screenContext);
            Log($"Suggestion received: '{suggestion}'");
            
            // If the user typed something while we were waiting for the LLM, abort!
            if (currentRequest != _requestCounter)
            {
                Log("Request ignored due to user typing.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                Dispatcher.Invoke(() =>
                {
                    if (currentRequest != _requestCounter) return;

                    _currentSuggestion = suggestion;
                    SuggestionText.Text = suggestion;
                    
                    var caretPos = CaretTracker.GetCaretPosition();
                    Log($"Caret pos: {caretPos}");
                    if (caretPos.HasValue)
                    {
                        this.Left = caretPos.Value.X;
                        this.Top = caretPos.Value.Y + 20;
                    }
                    else
                    {
                        this.Left = SystemParameters.PrimaryScreenWidth / 2 - 200;
                        this.Top = SystemParameters.PrimaryScreenHeight - 150;
                    }
                    
                    MainBorder.Visibility = Visibility.Visible;
                });
            }
        }

        private void KeyboardHook_OnAcceptSuggestion()
        {
            Dispatcher.Invoke(() =>
            {
                if (MainBorder.Visibility == Visibility.Visible && !string.IsNullOrEmpty(_currentSuggestion))
                {
                    string textToInject = _currentSuggestion;
                    MainBorder.Visibility = Visibility.Hidden;
                    _currentSuggestion = "";
                    
                    _keyboardHook.Pause();
                    KeyboardHook.InjectText(textToInject);
                    _keyboardHook.Resume();
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_keyboardHook != null) _keyboardHook.Stop();
            if (_llmService != null) _llmService.Cleanup();
            base.OnClosed(e);
        }
    }
}

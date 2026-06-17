using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MinimaxTTSManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                ConfigureNetwork();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "启动失败: " + ex.Message + Environment.NewLine + Environment.NewLine + ex,
                    "MinimaxTTSManager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        static void ConfigureNetwork()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 16;
            ServicePointManager.UseNagleAlgorithm = false;
        }
    }

    sealed class AppConfig
    {
        public string Provider = "minimax";
        public string ApiKey = "";
        public string BaseUrl = "https://api.minimaxi.com";
        public string Model = "speech-2.8-hd";
        public string DefaultVoice = "English_radiant_girl";
        public int Port = 5050;
        public int TimeoutMs = 30000;
        public int CacheEntries = 64;
        public int MaxConcurrent = 2;
        public string VoicesJsonPath = "";
        public string SapiDllPath = "";
        public string RegisterScriptPath = "";
        public string VoiceLines =
            "English compelling lady|English_compelling_lady1|0409|Female\r\n" +
            "English radiant girl|English_radiant_girl|0409|Female\r\n" +
            "English narrator|English_expressive_narrator|0409|Female\r\n" +
            "English magnetic man|English_magnetic_voiced_man|0409|Male\r\n" +
            "Mandarin executive|Chinese (Mandarin)_Reliable_Executive|0804|Male\r\n" +
            "Mandarin news anchor|Chinese (Mandarin)_News_Anchor|0804|Female\r\n" +
            "Japanese senior|Japanese_IntellectualSenior|0411|Male\r\n" +
            "Japanese princess|Japanese_DecisivePrincess|0411|Female";
    }

    sealed class VoiceEntry
    {
        public string Token = "";
        public string Name = "";
        public string Lang = "0804";
        public string Gender = "Female";
        public string Provider = "minimax";
        public string Model = "speech-2.8-hd";
        public string Voice = "";
        public string Endpoint = "http://127.0.0.1:5050";
        public string Path = "/v1/speech";
        public string Rate = "1.0";
        public string TimeoutMs = "20000";
    }

    sealed class ProviderPreset
    {
        public string Key = "";
        public string Name = "";
        public string BaseUrl = "";
        public string[] Models = new string[0];
        public bool MiniMaxNative;

        public override string ToString()
        {
            return Name;
        }
    }

    sealed class VoicePreset
    {
        public string Name = "";
        public string VoiceId = "";
        public string Lang = "0804";
        public string Gender = "Female";

        public override string ToString()
        {
            return Name;
        }
    }

    sealed class MainForm : Form
    {
        static readonly ProviderPreset[] ProviderPresets = new[]
        {
            new ProviderPreset
            {
                Key = "minimax",
                Name = "MiniMax",
                BaseUrl = "https://api.minimaxi.com",
                Models = new[] { "speech-2.8-hd", "speech-2.8-turbo", "speech-01-hd", "speech-01-turbo" },
                MiniMaxNative = true
            },
            new ProviderPreset
            {
                Key = "glm",
                Name = "GLM / OpenAI Compatible",
                BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                Models = new[] { "cogtts", "tts-1", "tts-1-hd" },
                MiniMaxNative = false
            },
            new ProviderPreset
            {
                Key = "openai-compatible",
                Name = "OpenAI Compatible",
                BaseUrl = "https://api.openai.com/v1",
                Models = new[] { "tts-1", "tts-1-hd", "gpt-4o-mini-tts" },
                MiniMaxNative = false
            },
            new ProviderPreset
            {
                Key = "custom",
                Name = "Custom TTS",
                BaseUrl = "",
                Models = new[] { "tts-1" },
                MiniMaxNative = false
            }
        };

        static readonly VoicePreset[] DefaultVoicePresets = new[]
        {
            new VoicePreset { Name = "English compelling lady", VoiceId = "English_compelling_lady1", Lang = "0409", Gender = "Female" },
            new VoicePreset { Name = "English radiant girl", VoiceId = "English_radiant_girl", Lang = "0409", Gender = "Female" },
            new VoicePreset { Name = "English narrator", VoiceId = "English_expressive_narrator", Lang = "0409", Gender = "Female" },
            new VoicePreset { Name = "English magnetic man", VoiceId = "English_magnetic_voiced_man", Lang = "0409", Gender = "Male" },
            new VoicePreset { Name = "Mandarin executive", VoiceId = "Chinese (Mandarin)_Reliable_Executive", Lang = "0804", Gender = "Male" },
            new VoicePreset { Name = "Mandarin news anchor", VoiceId = "Chinese (Mandarin)_News_Anchor", Lang = "0804", Gender = "Female" },
            new VoicePreset { Name = "Japanese senior", VoiceId = "Japanese_IntellectualSenior", Lang = "0411", Gender = "Male" },
            new VoicePreset { Name = "Japanese princess", VoiceId = "Japanese_DecisivePrincess", Lang = "0411", Gender = "Female" }
        };

        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly string configDir;
        readonly string configPath;
        AppConfig config;
        TtsHttpServer server;

        ComboBox providerBox;
        TextBox apiKeyBox;
        TextBox baseUrlBox;
        ComboBox modelBox;
        ComboBox defaultVoiceBox;
        NumericUpDown portBox;
        NumericUpDown timeoutBox;
        NumericUpDown cacheBox;
        NumericUpDown concurrentBox;
        TextBox voicesJsonBox;
        TextBox sapiDllBox;
        TextBox registerScriptBox;
        CheckedListBox voicePresetList;
        TextBox customVoiceBox;
        ComboBox customLangBox;
        ComboBox customGenderBox;
        Panel advancedPanel;
        Button advancedButton;
        Label statusLabel;
        TextBox logBox;
        Button startButton;
        Button stopButton;
        bool loadingUi;

        readonly Color ink = Color.FromArgb(20, 17, 10);
        readonly Color paperInk = Color.FromArgb(232, 230, 240);
        readonly Color dim = Color.FromArgb(91, 87, 125);
        readonly Color bg = Color.FromArgb(7, 6, 13);
        readonly Color voidPanel = Color.FromArgb(10, 14, 39);
        readonly Color gutter = Color.FromArgb(27, 35, 64);
        readonly Color panelBg = Color.FromArgb(253, 243, 218);
        readonly Color fieldBg = Color.FromArgb(255, 249, 230);
        readonly Color accent = Color.FromArgb(0, 200, 150);
        readonly Color hot = Color.FromArgb(255, 176, 0);
        readonly Color purple = Color.FromArgb(176, 102, 255);
        readonly Color warning = Color.FromArgb(255, 51, 102);

        public MainForm()
        {
            Text = "MiniMax SAPI5 TTS Manager";
            MinimumSize = new Size(900, 700);
            Size = new Size(980, 760);
            StartPosition = FormStartPosition.CenterScreen;

            configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MinimaxTTS");
            Directory.CreateDirectory(configDir);
            configPath = Path.Combine(configDir, "config.json");
            config = LoadConfig();
            EnsureDefaultPaths(config);

            BuildUi();
            LoadToUi();
            UpdateStatus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { server?.Stop(); } catch { }
            SaveFromUi();
            SaveConfig();
            base.OnFormClosing(e);
        }

        AppConfig LoadConfig()
        {
            try
            {
                if (!File.Exists(configPath)) return new AppConfig();
                var text = File.ReadAllText(configPath, Encoding.UTF8);
                var loaded = json.Deserialize<AppConfig>(text);
                return loaded ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        void SaveConfig()
        {
            Directory.CreateDirectory(configDir);
            File.WriteAllText(configPath, json.Serialize(config), Encoding.UTF8);
        }

        void EnsureDefaultPaths(AppConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(cfg.VoicesJsonPath))
                cfg.VoicesJsonPath = Path.Combine(configDir, "voices.json");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            var candidates = new[]
            {
                Path.Combine(baseDir, "http_sapi5_engine.dll"),
                Path.Combine(baseDir, "sapi5", "http_sapi5_engine.dll"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "sapi5", "http_sapi5_engine_v2.dll")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "sapi5", "http_sapi5_engine.dll"))
            };
            if (string.IsNullOrWhiteSpace(cfg.SapiDllPath) || !File.Exists(cfg.SapiDllPath))
            {
                cfg.SapiDllPath = candidates.FirstOrDefault(File.Exists) ?? cfg.SapiDllPath;
            }

            var scripts = new[]
            {
                Path.Combine(baseDir, "register-sapi5-voices.ps1"),
                Path.Combine(baseDir, "sapi5", "register-sapi5-voices.ps1"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "sapi5", "register-sapi5-voices.ps1"))
            };
            if (string.IsNullOrWhiteSpace(cfg.RegisterScriptPath) || !File.Exists(cfg.RegisterScriptPath))
            {
                cfg.RegisterScriptPath = scripts.FirstOrDefault(File.Exists) ?? cfg.RegisterScriptPath;
            }
        }

        void BuildUi()
        {
            BackColor = bg;
            ForeColor = ink;
            Font = new Font("Consolas", 9.5f, FontStyle.Regular);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(14),
                BackColor = bg
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            Controls.Add(root);

            var header = PixelPanel();
            header.Height = 66;
            header.Dock = DockStyle.Top;
            var title = new Label
            {
                Text = "MINIMAX · SAPI5 TTS",
                Dock = DockStyle.Top,
                ForeColor = paperInk,
                Font = new Font("Consolas", 18, FontStyle.Bold),
                Padding = new Padding(14, 10, 14, 0),
                Height = 38
            };
            var subtitle = new Label
            {
                Text = "AUTO VOICE BRIDGE / LOCAL SERVER",
                Dock = DockStyle.Top,
                ForeColor = hot,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Padding = new Padding(16, 0, 14, 8),
                Height = 22
            };
            header.Controls.Add(subtitle);
            header.Controls.Add(title);
            root.Controls.Add(header, 0, 0);

            var providerPanel = PixelPanel("MODEL");
            providerPanel.AutoSize = true;
            providerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.Controls.Add(providerPanel, 0, 1);
            var providerGrid = PixelGrid(4);
            providerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            providerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            providerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            providerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            providerPanel.Controls.Add(providerGrid);

            providerBox = AddCombo(providerGrid, 0, 0, "Provider", true);
            providerBox.Items.AddRange(ProviderPresets);
            providerBox.SelectedIndexChanged += (s, e) => ApplyProviderPreset(false);
            modelBox = AddCombo(providerGrid, 0, 2, "Model", false);
            apiKeyBox = AddWideTextBox(providerGrid, 1, "API Key", true);
            baseUrlBox = AddWideTextBox(providerGrid, 2, "Base URL", false);
            defaultVoiceBox = AddWideCombo(providerGrid, 3, "Voice ID", false);

            var voicePanel = PixelPanel("VOICES");
            root.Controls.Add(voicePanel, 0, 2);
            var voiceGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10, 22, 10, 10),
                BackColor = panelBg
            };
            voiceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            voiceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            voiceGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            voiceGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            voicePanel.Controls.Add(voiceGrid);

            voiceGrid.Controls.Add(PixelCaption("PRESETS"), 0, 0);
            voiceGrid.Controls.Add(PixelCaption("CUSTOM"), 1, 0);

            voicePresetList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BackColor = fieldBg,
                ForeColor = ink,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 10)
            };
            voicePresetList.Items.AddRange(DefaultVoicePresets);
            voicePresetList.ItemCheck += (s, e) =>
            {
                if (loadingUi) return;
                if (IsHandleCreated)
                    BeginInvoke(new Action(RefreshDefaultVoiceOptions));
                else
                    RefreshDefaultVoiceOptions();
            };
            voiceGrid.Controls.Add(voicePresetList, 0, 1);

            var customPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Margin = new Padding(10, 0, 0, 0),
                BackColor = panelBg
            };
            customPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            customPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            customPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            customPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            customPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            voiceGrid.Controls.Add(customPanel, 1, 1);

            customVoiceBox = PixelTextBox(true, false);
            customVoiceBox.ScrollBars = ScrollBars.Both;
            customVoiceBox.AcceptsReturn = true;
            customVoiceBox.AcceptsTab = true;
            customVoiceBox.WordWrap = false;
            customVoiceBox.TextChanged += (s, e) => RefreshDefaultVoiceOptions();
            customPanel.Controls.Add(customVoiceBox, 0, 0);
            customPanel.SetColumnSpan(customVoiceBox, 3);

            customLangBox = PixelCombo();
            customLangBox.Items.AddRange(new object[] { "0409 en-US", "0804 zh-CN", "0411 ja-JP" });
            customLangBox.DropDownStyle = ComboBoxStyle.DropDownList;
            customLangBox.SelectedIndex = 0;
            customGenderBox = PixelCombo();
            customGenderBox.Items.AddRange(new object[] { "Female", "Male", "Neutral" });
            customGenderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            customGenderBox.SelectedIndex = 0;
            customPanel.Controls.Add(PixelCaption("LANG"), 0, 1);
            customPanel.Controls.Add(customLangBox, 1, 1);
            customPanel.Controls.Add(customGenderBox, 2, 1);

            var commandPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = bg,
                Padding = new Padding(0, 10, 0, 8)
            };
            root.Controls.Add(commandPanel, 0, 3);
            startButton = AddCommandButton(commandPanel, "START", StartServer, accent);
            AddCommandButton(commandPanel, "REGISTER SAPI5", RegisterSapi, hot);
            stopButton = AddCommandButton(commandPanel, "STOP", StopServer, warning);
            advancedButton = AddCommandButton(commandPanel, "ADVANCED", ToggleAdvanced, purple);

            advancedPanel = PixelPanel("ADVANCED");
            advancedPanel.Visible = false;
            root.Controls.Add(advancedPanel, 0, 4);
            var advancedGrid = PixelGrid(4);
            advancedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            advancedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            advancedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            advancedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            advancedPanel.Controls.Add(advancedGrid);

            portBox = AddNumber(advancedGrid, 0, 0, "Port", 1, 65535);
            timeoutBox = AddNumber(advancedGrid, 0, 2, "Timeout", 3000, 120000);
            concurrentBox = AddNumber(advancedGrid, 1, 0, "Concurrent", 1, 8);
            cacheBox = AddNumber(advancedGrid, 1, 2, "Cache", 0, 512);
            voicesJsonBox = AddPath(advancedGrid, 2, 0, "Voices JSON", true);
            sapiDllBox = AddPath(advancedGrid, 2, 2, "SAPI DLL", false);
            registerScriptBox = AddPath(advancedGrid, 3, 0, "Script", false);
            AddSmallButton(advancedGrid, 3, 2, "SAVE", () => { SaveFromUi(); SaveConfig(); Log("配置已保存: " + configPath); });
            AddSmallButton(advancedGrid, 4, 0, "GENERATE JSON", GenerateVoicesJson);
            AddSmallButton(advancedGrid, 4, 2, "TEST VOICE", TestVoice);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = bg };
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(bottom, 0, 5);

            statusLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                ForeColor = hot,
                Padding = new Padding(2, 0, 0, 5),
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            bottom.Controls.Add(statusLabel, 0, 0);

            logBox = PixelTextBox(true, false);
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            bottom.Controls.Add(logBox, 0, 1);
        }

        Panel PixelPanel(string caption = null)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = string.IsNullOrWhiteSpace(caption) ? voidPanel : panelBg,
                BorderStyle = BorderStyle.Fixed3D,
                Margin = new Padding(0, 0, 0, 10)
            };
            if (!string.IsNullOrWhiteSpace(caption))
            {
                var label = PixelCaption(" " + caption + " ");
                label.Dock = DockStyle.Top;
                label.ForeColor = hot;
                label.BackColor = gutter;
                label.Height = 22;
                panel.Controls.Add(label);
                panel.ControlAdded += (s, e) => label.BringToFront();
            }
            return panel;
        }

        Label PixelCaption(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = accent,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(0, 2, 0, 4)
            };
        }

        TableLayoutPanel PixelGrid(int rows)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = rows,
                AutoSize = true,
                Padding = new Padding(10, 24, 10, 10),
                BackColor = panelBg
            };
            for (int i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return grid;
        }

        Label AddGridLabel(TableLayoutPanel grid, int row, int col, string text)
        {
            EnsureRow(grid, row);
            var lab = new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = ink,
                Padding = new Padding(0, 7, 0, 0),
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            grid.Controls.Add(lab, col, row);
            return lab;
        }

        TextBox AddTextBox(TableLayoutPanel grid, int row, int col, string label, bool password)
        {
            AddGridLabel(grid, row, col, label);
            var box = PixelTextBox(false, password);
            grid.Controls.Add(box, col + 1, row);
            return box;
        }

        TextBox AddWideTextBox(TableLayoutPanel grid, int row, string label, bool password)
        {
            AddGridLabel(grid, row, 0, label);
            var box = PixelTextBox(false, password);
            grid.Controls.Add(box, 1, row);
            grid.SetColumnSpan(box, 3);
            return box;
        }

        ComboBox AddCombo(TableLayoutPanel grid, int row, int col, string label, bool listOnly)
        {
            AddGridLabel(grid, row, col, label);
            var box = PixelCombo();
            box.DropDownStyle = listOnly ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown;
            grid.Controls.Add(box, col + 1, row);
            return box;
        }

        ComboBox AddWideCombo(TableLayoutPanel grid, int row, string label, bool listOnly)
        {
            AddGridLabel(grid, row, 0, label);
            var box = PixelCombo();
            box.DropDownStyle = listOnly ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown;
            grid.Controls.Add(box, 1, row);
            grid.SetColumnSpan(box, 3);
            return box;
        }

        NumericUpDown AddNumber(TableLayoutPanel grid, int row, int col, string label, int min, int max)
        {
            AddGridLabel(grid, row, col, label);
            var box = new NumericUpDown
            {
                Dock = DockStyle.Left,
                Minimum = min,
                Maximum = max,
                Width = 130,
                BackColor = fieldBg,
                ForeColor = ink,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 10)
            };
            grid.Controls.Add(box, col + 1, row);
            return box;
        }

        TextBox AddPath(TableLayoutPanel grid, int row, int col, string label, bool saveFile)
        {
            AddGridLabel(grid, row, col, label);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, BackColor = panelBg };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            var box = PixelTextBox(false, false);
            var browse = PixelButton("...", dim);
            browse.Width = 30;
            browse.Height = 26;
            browse.Click += (s, e) =>
            {
                if (saveFile)
                {
                    using (var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*", FileName = Path.GetFileName(box.Text) })
                    {
                        if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
                    }
                }
                else
                {
                    using (var dialog = new OpenFileDialog { Filter = "All files (*.*)|*.*", FileName = Path.GetFileName(box.Text) })
                    {
                        if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
                    }
                }
            };
            panel.Controls.Add(box, 0, 0);
            panel.Controls.Add(browse, 1, 0);
            grid.Controls.Add(panel, col + 1, row);
            return box;
        }

        void AddSmallButton(TableLayoutPanel grid, int row, int col, string text, Action action)
        {
            EnsureRow(grid, row);
            var button = PixelButton(text, dim);
            button.Dock = DockStyle.Left;
            button.Width = 138;
            button.Click += (s, e) => action();
            grid.Controls.Add(button, col + 1, row);
        }

        TextBox PixelTextBox(bool multiline, bool password)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = multiline,
                UseSystemPasswordChar = password,
                BackColor = fieldBg,
                ForeColor = ink,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 10)
            };
        }

        ComboBox PixelCombo()
        {
            return new ComboBox
            {
                Dock = DockStyle.Fill,
                BackColor = fieldBg,
                ForeColor = ink,
                FlatStyle = FlatStyle.Popup,
                Font = new Font("Consolas", 10)
            };
        }

        Button PixelButton(string text, Color color)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = false,
                Height = 32,
                BackColor = color,
                ForeColor = ink,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Margin = new Padding(0, 0, 8, 0)
            };
            b.FlatAppearance.BorderColor = ink;
            b.FlatAppearance.BorderSize = 3;
            return b;
        }

        Button AddCommandButton(FlowLayoutPanel panel, string text, Action action, Color color)
        {
            var b = PixelButton(text, color);
            b.Width = 150;
            b.Click += (s, e) => action();
            panel.Controls.Add(b);
            return b;
        }

        void EnsureRow(TableLayoutPanel grid, int row)
        {
            while (grid.RowCount <= row)
            {
                grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
        }

        void LoadToUi()
        {
            loadingUi = true;
            var provider = FindProvider(config.Provider);
            providerBox.SelectedItem = provider;
            ApplyProviderPreset(true);
            apiKeyBox.Text = config.ApiKey;
            baseUrlBox.Text = config.BaseUrl;
            modelBox.Text = config.Model;
            portBox.Value = Clamp(config.Port, 1, 65535);
            timeoutBox.Value = Clamp(config.TimeoutMs, 3000, 120000);
            cacheBox.Value = Clamp(config.CacheEntries, 0, 512);
            concurrentBox.Value = Clamp(config.MaxConcurrent, 1, 8);
            voicesJsonBox.Text = config.VoicesJsonPath;
            sapiDllBox.Text = config.SapiDllPath;
            registerScriptBox.Text = config.RegisterScriptPath;
            LoadVoicePickerFromLines(config.VoiceLines);
            RefreshDefaultVoiceOptions();
            defaultVoiceBox.Text = config.DefaultVoice;
            loadingUi = false;
        }

        void SaveFromUi()
        {
            config.Provider = GetProviderKey();
            config.ApiKey = apiKeyBox.Text.Trim();
            config.BaseUrl = NormalizeBaseUrl(baseUrlBox.Text.Trim(), config.Provider);
            config.Model = modelBox.Text.Trim();
            config.DefaultVoice = defaultVoiceBox.Text.Trim();
            config.Port = (int)portBox.Value;
            config.TimeoutMs = (int)timeoutBox.Value;
            config.CacheEntries = (int)cacheBox.Value;
            config.MaxConcurrent = (int)concurrentBox.Value;
            config.VoicesJsonPath = voicesJsonBox.Text.Trim();
            config.SapiDllPath = sapiDllBox.Text.Trim();
            config.RegisterScriptPath = registerScriptBox.Text.Trim();
            config.VoiceLines = BuildVoiceLinesFromPicker();
        }

        decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        void ToggleAdvanced()
        {
            advancedPanel.Visible = !advancedPanel.Visible;
            advancedButton.Text = advancedPanel.Visible ? "HIDE ADVANCED" : "ADVANCED";
        }

        ProviderPreset FindProvider(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) key = "minimax";
            return ProviderPresets.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? ProviderPresets[0];
        }

        string GetProviderKey()
        {
            var selected = providerBox.SelectedItem as ProviderPreset;
            return selected != null ? selected.Key : "minimax";
        }

        void ApplyProviderPreset(bool keepUserValues)
        {
            if (providerBox.SelectedItem == null && providerBox.Items.Count > 0)
                providerBox.SelectedIndex = 0;

            var preset = providerBox.SelectedItem as ProviderPreset ?? ProviderPresets[0];
            var model = modelBox.Text;
            modelBox.Items.Clear();
            modelBox.Items.AddRange(preset.Models);
            if (keepUserValues && !string.IsNullOrWhiteSpace(model))
                modelBox.Text = model;
            else if (preset.Models.Length > 0)
                modelBox.Text = preset.Models[0];

            if (!keepUserValues || string.IsNullOrWhiteSpace(baseUrlBox.Text))
                baseUrlBox.Text = preset.BaseUrl;

        }

        void LoadVoicePickerFromLines(string text)
        {
            for (int i = 0; i < voicePresetList.Items.Count; i++)
                voicePresetList.SetItemChecked(i, false);

            var custom = new List<string>();
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                var voiceId = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();
                var lang = parts.Length > 2 ? parts[2].Trim() : "";
                int presetIndex = FindVoicePresetIndex(voiceId, lang);
                if (presetIndex >= 0)
                {
                    voicePresetList.SetItemChecked(presetIndex, true);
                }
                else
                {
                    custom.Add(line);
                }
            }
            customVoiceBox.Text = string.Join(Environment.NewLine, custom);
        }

        int FindVoicePresetIndex(string voiceId, string lang)
        {
            for (int i = 0; i < DefaultVoicePresets.Length; i++)
            {
                var preset = DefaultVoicePresets[i];
                if (string.Equals(preset.VoiceId, voiceId, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(lang) || string.Equals(preset.Lang, lang, StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
            return -1;
        }

        string BuildVoiceLinesFromPicker()
        {
            var lines = new List<string>();
            foreach (var item in voicePresetList.CheckedItems)
            {
                var voice = item as VoicePreset;
                if (voice == null) continue;
                lines.Add(voice.Name + "|" + voice.VoiceId + "|" + voice.Lang + "|" + voice.Gender);
            }

            foreach (var raw in (customVoiceBox.Text ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.Contains("|"))
                {
                    lines.Add(line);
                }
                else
                {
                    lines.Add(FriendlyVoiceName(line) + "|" + line + "|" + SelectedCustomLang() + "|" + SelectedCustomGender());
                }
            }

            if (lines.Count == 0 && !string.IsNullOrWhiteSpace(defaultVoiceBox.Text))
            {
                var voice = defaultVoiceBox.Text.Trim();
                lines.Add(FriendlyVoiceName(voice) + "|" + voice + "|" + SelectedCustomLang() + "|" + SelectedCustomGender());
            }
            return string.Join(Environment.NewLine, lines);
        }

        void RefreshDefaultVoiceOptions()
        {
            if (defaultVoiceBox == null) return;
            var current = defaultVoiceBox.Text;
            var voices = new List<string>();
            foreach (var item in voicePresetList.CheckedItems)
            {
                var voice = item as VoicePreset;
                if (voice != null && !voices.Contains(voice.VoiceId)) voices.Add(voice.VoiceId);
            }
            foreach (var raw in (customVoiceBox.Text ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                var voiceId = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();
                if (!string.IsNullOrWhiteSpace(voiceId) && !voices.Contains(voiceId)) voices.Add(voiceId);
            }

            defaultVoiceBox.Items.Clear();
            defaultVoiceBox.Items.AddRange(voices.Cast<object>().ToArray());
            if (!string.IsNullOrWhiteSpace(current))
                defaultVoiceBox.Text = current;
            else if (voices.Count > 0)
                defaultVoiceBox.Text = voices[0];
        }

        string SelectedCustomLang()
        {
            var text = Convert.ToString(customLangBox.SelectedItem ?? "0409");
            return text.Length >= 4 ? text.Substring(0, 4) : "0409";
        }

        string SelectedCustomGender()
        {
            return Convert.ToString(customGenderBox.SelectedItem ?? "Female");
        }

        string FriendlyVoiceName(string voiceId)
        {
            var value = (voiceId ?? "").Trim();
            if (value.Length == 0) return "Custom Voice";
            value = value.Replace("_", " ").Replace("-", " ");
            while (value.Contains("  ")) value = value.Replace("  ", " ");
            return value.Length > 42 ? value.Substring(0, 42) : value;
        }

        string NormalizeBaseUrl(string value, string provider)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                var preset = FindProvider(provider);
                value = string.IsNullOrWhiteSpace(preset.BaseUrl) ? "https://api.openai.com/v1" : preset.BaseUrl;
            }
            value = value.Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }
            if (string.Equals(provider, "minimax", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Replace("api.minimax.com", "api.minimaxi.com");
                value = value.Replace("api.minimax.io", "api.minimaxi.com");
            }
            return value.TrimEnd('/', '\\');
        }

        void StartServer()
        {
            SaveFromUi();
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                MessageBox.Show(this, "请先填写 MiniMax API Key。", "缺少配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(config.DefaultVoice))
            {
                var first = ParseVoiceLines().FirstOrDefault();
                if (first != null)
                {
                    config.DefaultVoice = first.Voice;
                    defaultVoiceBox.Text = config.DefaultVoice;
                }
            }

            if (!IsPortAvailable(config.Port))
            {
                int freePort = FindFreePort(config.Port + 1);
                var msg = "端口 " + config.Port + " 已经被占用，服务无法启动。\n\n" +
                          "通常是旧的 Python 服务或另一个管理器窗口还在运行。\n\n" +
                          "是否切换到空闲端口 " + freePort + "？\n" +
                          "注意：切换端口后需要重新生成 voices.json 并重新注册 SAPI5。";
                if (MessageBox.Show(this, msg, "端口被占用", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    Log("启动取消：端口 " + config.Port + " 已被占用。");
                    return;
                }
                config.Port = freePort;
                portBox.Value = freePort;
                SaveConfig();
                Log("已切换到空闲端口 " + freePort + "。请重新生成 voices.json 并重新注册 SAPI5。");
            }

            try
            {
                server?.Stop();
                server = new TtsHttpServer(config, Log);
                server.Start();
                SaveConfig();
                Log("服务已启动: http://127.0.0.1:" + config.Port + "/v1/speech");
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Log("启动失败: " + ex.Message);
                MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static bool IsPortAvailable(int port)
        {
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { probe?.Stop(); } catch { }
            }
        }

        static int FindFreePort(int startPort)
        {
            for (int port = Math.Max(1, startPort); port <= 65535; port++)
            {
                if (IsPortAvailable(port)) return port;
            }
            throw new InvalidOperationException("找不到可用端口。");
        }

        void StopServer()
        {
            try { server?.Stop(); } catch { }
            server = null;
            Log("服务已停止");
            UpdateStatus();
        }

        async void TestVoice()
        {
            SaveFromUi();
            if (string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.DefaultVoice))
            {
                MessageBox.Show(this, "请填写 API Key 和默认 Voice ID。", "缺少配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Log("开始测试默认音色...");
            try
            {
                var bytes = await Task.Run(() => MinimaxClient.Synthesize(config, "这是 SAPI5 TTS 语音测试。", config.DefaultVoice, 1.0, "wav", config.Model, config.Provider));
                var wavPath = Path.Combine(configDir, "test.wav");
                File.WriteAllBytes(wavPath, bytes);
                using (var player = new SoundPlayer(wavPath))
                {
                    player.Play();
                }
                Log("测试成功: " + wavPath + " (" + bytes.Length + " bytes)");
            }
            catch (Exception ex)
            {
                Log("测试失败: " + ex.Message);
                MessageBox.Show(this, ex.Message, "测试失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void GenerateVoicesJson()
        {
            TryGenerateVoicesJson();
        }

        bool TryGenerateVoicesJson()
        {
            SaveFromUi();
            var voices = ParseVoiceLines();
            if (voices.Count == 0)
            {
                MessageBox.Show(this, "请至少填写一行音色。", "没有音色", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (voices.Any(v => IsPlaceholderVoice(v.Voice)))
            {
                MessageBox.Show(this, "音色列表里还有 replace_with_minimax_voice_id 占位符。请改成真实 MiniMax voice_id，或先填写默认 Voice ID。", "音色未配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var payload = new Dictionary<string, object>
            {
                { "voices", voices.Select(ToJsonVoice).ToList() }
            };
            var dir = Path.GetDirectoryName(config.VoicesJsonPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(config.VoicesJsonPath, json.Serialize(payload), Encoding.UTF8);
            SaveConfig();
            Log("已生成 voices.json: " + config.VoicesJsonPath + " (" + voices.Count + " voices)");
            return true;
        }

        List<VoiceEntry> ParseVoiceLines()
        {
            var result = new List<VoiceEntry>();
            var lines = (config.VoiceLines ?? "").Replace("\r", "").Split('\n');
            int i = 1;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                var name = parts.Length > 0 ? parts[0].Trim() : "";
                var voice = parts.Length > 1 ? parts[1].Trim() : name;
                var lang = parts.Length > 2 ? parts[2].Trim() : "0804";
                var gender = parts.Length > 3 ? parts[3].Trim() : "Female";
                if (IsPlaceholderVoice(voice) && !string.IsNullOrWhiteSpace(config.DefaultVoice))
                {
                    voice = config.DefaultVoice;
                }
                if (string.IsNullOrWhiteSpace(voice)) continue;
                if (string.IsNullOrWhiteSpace(name)) name = voice;
                result.Add(new VoiceEntry
                {
                    Token = "MinimaxTTS-mm-" + SanitizeToken(name, i),
                    Name = "MinimaxTTS - " + name,
                    Lang = lang,
                    Gender = gender,
                    Provider = config.Provider,
                    Model = config.Model,
                    Voice = voice,
                    Endpoint = "http://127.0.0.1:" + config.Port,
                    Path = "/v1/speech",
                    Rate = "1.0",
                    TimeoutMs = config.TimeoutMs.ToString()
                });
                i++;
            }
            return result;
        }

        static bool IsPlaceholderVoice(string voice)
        {
            return string.Equals((voice ?? "").Trim(), "replace_with_minimax_voice_id", StringComparison.OrdinalIgnoreCase);
        }

        Dictionary<string, object> ToJsonVoice(VoiceEntry v)
        {
            return new Dictionary<string, object>
            {
                { "Token", v.Token },
                { "Name", v.Name },
                { "Lang", v.Lang },
                { "Gender", v.Gender },
                { "Provider", v.Provider },
                { "Model", v.Model },
                { "Voice", v.Voice },
                { "Endpoint", v.Endpoint },
                { "Path", v.Path },
                { "Rate", v.Rate },
                { "TimeoutMs", v.TimeoutMs }
            };
        }

        string SanitizeToken(string text, int index)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
                else if (c == '-' || c == '_' || c == '.')
                    sb.Append(c);
            }
            if (sb.Length == 0) sb.Append("voice").Append(index);
            return sb.ToString();
        }

        void RegisterSapi()
        {
            SaveFromUi();
            if (!File.Exists(config.RegisterScriptPath))
            {
                MessageBox.Show(this, "找不到注册脚本: " + config.RegisterScriptPath, "路径错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!File.Exists(config.SapiDllPath))
            {
                MessageBox.Show(this, "找不到 SAPI DLL: " + config.SapiDllPath, "路径错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!TryGenerateVoicesJson())
            {
                return;
            }

            try
            {
                var runnerPath = Path.Combine(configDir, "register-sapi5-admin.ps1");
                File.WriteAllText(runnerPath, BuildAdminRegistrationScript(), new UTF8Encoding(false));
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -NoExit -File " + QuoteArg(runnerPath),
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = configDir
                };
                Process.Start(psi);
                Log("已写入并启动管理员注册脚本: " + runnerPath);
                Log("如果没有弹出 UAC，请右键该脚本并选择“使用 PowerShell 运行”或手动以管理员身份运行。");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                    Log("管理员注册已取消。");
                else
                    Log("注册启动失败: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("注册启动失败: " + ex.Message);
            }
        }

        string BuildAdminRegistrationScript()
        {
            var lines = new[]
            {
                "$ErrorActionPreference = 'Stop'",
                "try {",
                "  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force | Out-Null",
                "  Write-Host 'MiniMax TTS SAPI5 registration (HKLM)' -ForegroundColor Cyan",
                "  Write-Host 'Script: " + EscapePs(config.RegisterScriptPath) + "'",
                "  Write-Host 'DLL:    " + EscapePs(config.SapiDllPath) + "'",
                "  Write-Host 'Voices: " + EscapePs(config.VoicesJsonPath) + "'",
                "  Write-Host ''",
                "  & '" + EscapePs(config.RegisterScriptPath) + "' -Unregister -Hive HKLM -VoicesJson '" + EscapePs(config.VoicesJsonPath) + "'",
                "  & '" + EscapePs(config.RegisterScriptPath) + "' -Hive HKLM -DllPath '" + EscapePs(config.SapiDllPath) + "' -VoicesJson '" + EscapePs(config.VoicesJsonPath) + "'",
                "  Write-Host ''",
                "  Write-Host 'Mirroring current voices into HKCU for user-level SAPI clients...' -ForegroundColor Cyan",
                "  & '" + EscapePs(config.RegisterScriptPath) + "' -Unregister -Hive HKCU -VoicesJson '" + EscapePs(config.VoicesJsonPath) + "'",
                "  & '" + EscapePs(config.RegisterScriptPath) + "' -Hive HKCU -DllPath '" + EscapePs(config.SapiDllPath) + "' -VoicesJson '" + EscapePs(config.VoicesJsonPath) + "'",
                "  Write-Host ''",
                "  Write-Host 'Registry tokens now:' -ForegroundColor Cyan",
                "  $root = 'Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech\\Voices\\Tokens'",
                "  if (Test-Path $root) {",
                "    Get-ChildItem $root | Where-Object { $_.PSChildName -like 'MinimaxTTS-*' } | ForEach-Object { Write-Host ('  + ' + $_.PSChildName) -ForegroundColor Green }",
                "  }",
                "  $inproc = 'Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\CLSID\\{72A7DE4B-327C-4EB8-A402-A280F4F41A05}\\InprocServer32'",
                "  if (Test-Path $inproc) { Write-Host ('COM DLL: ' + (Get-Item $inproc).GetValue('')) -ForegroundColor Green }",
                "  Write-Host ''",
                "  Write-Host 'Done. Restart Windows Settings and Zotero before checking the voice list.' -ForegroundColor Green",
                "} catch {",
                "  Write-Host ''",
                "  Write-Host 'Registration failed:' -ForegroundColor Red",
                "  Write-Host $_.Exception.Message -ForegroundColor Red",
                "  if ($_.ScriptStackTrace) { Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray }",
                "}",
                "Write-Host ''",
                "Read-Host 'Press Enter to close'"
            };
            return string.Join(Environment.NewLine, lines);
        }

        static string QuoteArg(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        static string EscapePs(string path)
        {
            return (path ?? "").Replace("'", "''");
        }

        void UpdateStatus()
        {
            bool running = server != null && server.Running;
            statusLabel.Text = running
                ? "状态: 服务运行中，监听 http://127.0.0.1:" + config.Port
                : "状态: 服务未启动";
            statusLabel.ForeColor = running ? Color.DarkGreen : Color.DarkRed;
            startButton.Enabled = !running;
            stopButton.Enabled = running;
        }

        void Log(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), message);
                return;
            }
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss ") + message + Environment.NewLine);
            UpdateStatus();
        }
    }

    sealed class TtsHttpServer
    {
        readonly AppConfig config;
        readonly Action<string> log;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly object cacheLock = new object();
        readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        readonly Queue<string> cacheOrder = new Queue<string>();
        readonly SemaphoreSlim synthGate;
        TcpListener listener;
        CancellationTokenSource cts;

        public bool Running { get; private set; }

        public TtsHttpServer(AppConfig config, Action<string> log)
        {
            this.config = CloneConfig(config);
            this.log = log;
            synthGate = new SemaphoreSlim(Math.Max(1, config.MaxConcurrent));
        }

        public void Start()
        {
            cts = new CancellationTokenSource();
            listener = new TcpListener(IPAddress.Loopback, config.Port);
            listener.Start();
            Running = true;
            Task.Run(() => AcceptLoop(cts.Token));
        }

        public void Stop()
        {
            Running = false;
            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
        }

        static AppConfig CloneConfig(AppConfig src)
        {
            return new AppConfig
            {
                Provider = src.Provider,
                ApiKey = src.ApiKey,
                BaseUrl = src.BaseUrl,
                Model = src.Model,
                DefaultVoice = src.DefaultVoice,
                Port = src.Port,
                TimeoutMs = src.TimeoutMs,
                CacheEntries = src.CacheEntries,
                MaxConcurrent = src.MaxConcurrent
            };
        }

        async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                    Task.Run(() => HandleClient(client));
                }
                catch
                {
                    client?.Close();
                    if (!token.IsCancellationRequested) log("HTTP accept loop stopped unexpectedly.");
                }
            }
        }

        void HandleClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = config.TimeoutMs;
                client.SendTimeout = config.TimeoutMs;
                var stream = client.GetStream();
                try
                {
                    var request = ReadRequest(stream);
                    if (request == null)
                    {
                        WriteJson(stream, 400, new Dictionary<string, object> { { "error", "bad request" } });
                        return;
                    }
                    if (request.Method == "GET" && request.Path == "/health")
                    {
                        WriteJson(stream, 200, new Dictionary<string, object>
                        {
                            { "ok", true },
                            { "engine", "minimax-sapi5-manager" },
                            { "provider", config.Provider },
                            { "model", config.Model },
                            { "cache_entries", cache.Count }
                        });
                        return;
                    }
                    if (request.Method == "POST" && (request.Path == "/v1/speech" || request.Path == "/v1/audio/speech"))
                    {
                        HandleSpeech(stream, request.Body);
                        return;
                    }
                    WriteJson(stream, 404, new Dictionary<string, object> { { "error", "not found" } });
                }
                catch (Exception ex)
                {
                    try { WriteJson(stream, 500, new Dictionary<string, object> { { "error", ex.Message } }); } catch { }
                    log("请求失败: " + ex.Message);
                }
            }
        }

        void HandleSpeech(NetworkStream stream, string body)
        {
            var data = json.Deserialize<Dictionary<string, object>>(body ?? "{}") ?? new Dictionary<string, object>();
            string text = GetString(data, "input");
            if (string.IsNullOrWhiteSpace(text)) text = GetString(data, "text");
            string voice = GetString(data, "voice");
            if (string.IsNullOrWhiteSpace(voice)) voice = config.DefaultVoice;
            string model = GetString(data, "model");
            if (string.IsNullOrWhiteSpace(model)) model = config.Model;
            string provider = GetString(data, "provider");
            if (string.IsNullOrWhiteSpace(provider)) provider = config.Provider;
            string format = GetString(data, "response_format");
            if (string.IsNullOrWhiteSpace(format)) format = "wav";
            double speed = GetDouble(data, "speed", 1.0);

            if (string.IsNullOrWhiteSpace(text))
            {
                WriteJson(stream, 400, new Dictionary<string, object> { { "error", "input is required" } });
                return;
            }
            if (string.IsNullOrWhiteSpace(voice))
            {
                WriteJson(stream, 400, new Dictionary<string, object> { { "error", "voice is required" } });
                return;
            }

            string key = provider + "\n" + model + "\n" + voice + "\n" + speed.ToString("0.###") + "\n" + format + "\n" + text;
            byte[] audio = GetCache(key);
            if (audio == null)
            {
                synthGate.Wait(config.TimeoutMs);
                try
                {
                    audio = MinimaxClient.Synthesize(config, text, voice, speed, format, model, provider);
                    PutCache(key, audio);
                }
                finally
                {
                    synthGate.Release();
                }
            }

            WriteBytes(stream, 200, format == "mp3" ? "audio/mpeg" : "audio/wav", audio);
        }

        byte[] GetCache(string key)
        {
            if (config.CacheEntries <= 0) return null;
            lock (cacheLock)
            {
                byte[] bytes;
                return cache.TryGetValue(key, out bytes) ? bytes : null;
            }
        }

        void PutCache(string key, byte[] bytes)
        {
            if (config.CacheEntries <= 0) return;
            lock (cacheLock)
            {
                if (!cache.ContainsKey(key))
                {
                    cacheOrder.Enqueue(key);
                }
                cache[key] = bytes;
                while (cache.Count > config.CacheEntries && cacheOrder.Count > 0)
                {
                    var old = cacheOrder.Dequeue();
                    if (old != key) cache.Remove(old);
                }
            }
        }

        HttpRequest ReadRequest(NetworkStream stream)
        {
            var buffer = new List<byte>();
            var temp = new byte[4096];
            int headerEnd = -1;
            while (buffer.Count < 65536)
            {
                int n = stream.Read(temp, 0, temp.Length);
                if (n <= 0) break;
                buffer.AddRange(temp.Take(n));
                headerEnd = FindHeaderEnd(buffer);
                if (headerEnd >= 0) break;
            }
            if (headerEnd < 0) return null;

            string headerText = Encoding.ASCII.GetString(buffer.Take(headerEnd).ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return null;
            var first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf(':');
                if (idx <= 0) continue;
                var name = lines[i].Substring(0, idx).Trim();
                var value = lines[i].Substring(idx + 1).Trim();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(value, out contentLength);
            }
            int bodyStart = headerEnd + 4;
            var bodyBytes = buffer.Skip(bodyStart).ToList();
            while (bodyBytes.Count < contentLength)
            {
                int n = stream.Read(temp, 0, Math.Min(temp.Length, contentLength - bodyBytes.Count));
                if (n <= 0) break;
                bodyBytes.AddRange(temp.Take(n));
            }
            return new HttpRequest
            {
                Method = first[0].ToUpperInvariant(),
                Path = first[1].Split('?')[0],
                Body = Encoding.UTF8.GetString(bodyBytes.Take(contentLength).ToArray())
            };
        }

        int FindHeaderEnd(List<byte> bytes)
        {
            for (int i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                    return i - 3;
            }
            return -1;
        }

        void WriteJson(NetworkStream stream, int status, Dictionary<string, object> payload)
        {
            var data = Encoding.UTF8.GetBytes(json.Serialize(payload));
            WriteBytes(stream, status, "application/json; charset=utf-8", data);
        }

        void WriteBytes(NetworkStream stream, int status, string contentType, byte[] data)
        {
            var header = "HTTP/1.1 " + status + " " + Reason(status) + "\r\n" +
                         "Content-Type: " + contentType + "\r\n" +
                         "Content-Length: " + data.Length + "\r\n" +
                         "Connection: close\r\n\r\n";
            var head = Encoding.ASCII.GetBytes(header);
            stream.Write(head, 0, head.Length);
            stream.Write(data, 0, data.Length);
        }

        string Reason(int status)
        {
            if (status == 200) return "OK";
            if (status == 400) return "Bad Request";
            if (status == 404) return "Not Found";
            return "Internal Server Error";
        }

        static string GetString(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }

        static double GetDouble(Dictionary<string, object> data, string key, double fallback)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null) return fallback;
            double result;
            return double.TryParse(Convert.ToString(value), out result) ? result : fallback;
        }

        sealed class HttpRequest
        {
            public string Method;
            public string Path;
            public string Body;
        }
    }

    static class MinimaxClient
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static byte[] Synthesize(AppConfig config, string text, string voice, double speed, string format, string model, string provider)
        {
            provider = string.IsNullOrWhiteSpace(provider) ? "minimax" : provider.Trim().ToLowerInvariant();
            if (provider == "minimax")
                return SynthesizeMiniMax(config, text, voice, speed, format, model);
            return SynthesizeOpenAICompatible(config, text, voice, speed, format, model, provider);
        }

        static byte[] SynthesizeMiniMax(AppConfig config, string text, string voice, double speed, string format, string model)
        {
            if (string.IsNullOrWhiteSpace(config.ApiKey))
                throw new InvalidOperationException("MiniMax API Key is empty.");

            speed = Math.Max(0.5, Math.Min(2.0, speed));
            format = string.Equals(format, "mp3", StringComparison.OrdinalIgnoreCase) ? "mp3" : "wav";
            string url = config.BaseUrl.TrimEnd('/', '\\') + "/v1/t2a_v2";

            var audioSetting = new Dictionary<string, object>
            {
                { "sample_rate", 22050 },
                { "format", format },
                { "channel", 1 }
            };
            if (format == "mp3") audioSetting["bitrate"] = 128000;

            var payload = new Dictionary<string, object>
            {
                { "model", string.IsNullOrWhiteSpace(model) ? config.Model : model },
                { "text", text },
                { "stream", false },
                { "voice_setting", new Dictionary<string, object>
                    {
                        { "voice_id", voice },
                        { "speed", speed },
                        { "vol", 1 },
                        { "pitch", 0 }
                    }
                },
                { "audio_setting", audioSetting },
                { "output_format", "hex" },
                { "subtitle_enable", false },
                { "aigc_watermark", false }
            };

            var body = Encoding.UTF8.GetBytes(Json.Serialize(payload));
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Accept = "application/json";
            req.UserAgent = "MinimaxTTSManager/0.3";
            req.KeepAlive = false;
            req.ProtocolVersion = HttpVersion.Version11;
            req.ServicePoint.Expect100Continue = false;
            req.Headers["Authorization"] = "Bearer " + config.ApiKey;
            req.Timeout = config.TimeoutMs;
            req.ReadWriteTimeout = config.TimeoutMs;
            using (var rs = req.GetRequestStream())
            {
                rs.Write(body, 0, body.Length);
            }

            string responseText = SendRequest(req);

            var response = Json.Deserialize<Dictionary<string, object>>(responseText);
            object baseRespObj;
            if (response.TryGetValue("base_resp", out baseRespObj))
            {
                var baseResp = baseRespObj as Dictionary<string, object>;
                object codeObj;
                if (baseResp != null && baseResp.TryGetValue("status_code", out codeObj))
                {
                    int code;
                    if (int.TryParse(Convert.ToString(codeObj), out code) && code != 0)
                    {
                        object msgObj;
                        baseResp.TryGetValue("status_msg", out msgObj);
                        throw new InvalidOperationException("MiniMax error: " + Convert.ToString(msgObj ?? codeObj));
                    }
                }
            }

            var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : null;
            var hex = data != null && data.ContainsKey("audio") ? Convert.ToString(data["audio"]) : "";
            if (string.IsNullOrWhiteSpace(hex)) throw new InvalidOperationException("MiniMax returned empty audio.");
            return HexToBytes(hex);
        }

        static byte[] SynthesizeOpenAICompatible(AppConfig config, string text, string voice, double speed, string format, string model, string provider)
        {
            if (string.IsNullOrWhiteSpace(config.ApiKey))
                throw new InvalidOperationException(provider + " API Key is empty.");

            speed = Math.Max(0.25, Math.Min(4.0, speed));
            format = string.Equals(format, "mp3", StringComparison.OrdinalIgnoreCase) ? "mp3" : "wav";
            string url = BuildOpenAICompatibleSpeechUrl(config.BaseUrl);
            var payload = new Dictionary<string, object>
            {
                { "model", string.IsNullOrWhiteSpace(model) ? config.Model : model },
                { "input", text },
                { "voice", voice },
                { "response_format", format },
                { "speed", speed }
            };

            var body = Encoding.UTF8.GetBytes(Json.Serialize(payload));
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Accept = format == "mp3" ? "audio/mpeg" : "audio/wav";
            req.UserAgent = "MinimaxTTSManager/0.4";
            req.KeepAlive = false;
            req.ProtocolVersion = HttpVersion.Version11;
            req.ServicePoint.Expect100Continue = false;
            req.Headers["Authorization"] = "Bearer " + config.ApiKey;
            req.Timeout = config.TimeoutMs;
            req.ReadWriteTimeout = config.TimeoutMs;
            using (var rs = req.GetRequestStream())
            {
                rs.Write(body, 0, body.Length);
            }
            return SendBytes(req);
        }

        static string BuildOpenAICompatibleSpeechUrl(string baseUrl)
        {
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/', '\\');
            if (baseUrl.Length == 0) baseUrl = "https://api.openai.com/v1";
            if (baseUrl.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase))
                return baseUrl;
            if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                baseUrl.EndsWith("/v4", StringComparison.OrdinalIgnoreCase))
                return baseUrl + "/audio/speech";
            return baseUrl + "/v1/audio/speech";
        }

        static string SendRequest(HttpWebRequest req)
        {
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                var detail = ReadWebException(ex);
                if (!string.IsNullOrWhiteSpace(detail))
                    throw new InvalidOperationException(detail, ex);
                throw;
            }
        }

        static byte[] SendBytes(HttpWebRequest req)
        {
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (WebException ex)
            {
                var detail = ReadWebException(ex);
                if (!string.IsNullOrWhiteSpace(detail))
                    throw new InvalidOperationException(detail, ex);
                throw;
            }
        }

        static string ReadWebException(WebException ex)
        {
            var resp = ex.Response as HttpWebResponse;
            string body = "";
            try
            {
                if (ex.Response != null)
                {
                    using (var stream = ex.Response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }
                }
            }
            catch { }

            string prefix = resp != null
                ? "HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription
                : ex.Message;
            if (ex.InnerException != null)
            {
                prefix += " | " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
            }
            if (string.IsNullOrWhiteSpace(body)) return prefix;
            return prefix + ": " + SummarizeMiniMaxError(body);
        }

        static string SummarizeMiniMaxError(string body)
        {
            try
            {
                var obj = Json.Deserialize<Dictionary<string, object>>(body);
                object baseObj;
                if (obj != null && obj.TryGetValue("base_resp", out baseObj))
                {
                    var baseResp = baseObj as Dictionary<string, object>;
                    if (baseResp != null)
                    {
                        object codeObj;
                        object msgObj;
                        baseResp.TryGetValue("status_code", out codeObj);
                        baseResp.TryGetValue("status_msg", out msgObj);
                        return "MiniMax status_code=" + Convert.ToString(codeObj) + " status_msg=" + Convert.ToString(msgObj);
                    }
                }
                object errObj;
                if (obj != null && obj.TryGetValue("error", out errObj))
                    return Convert.ToString(errObj);
            }
            catch { }
            body = body.Replace("\r", " ").Replace("\n", " ").Trim();
            return body.Length > 500 ? body.Substring(0, 500) + "..." : body;
        }

        static byte[] HexToBytes(string hex)
        {
            hex = hex.Trim();
            if ((hex.Length & 1) == 1) hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}

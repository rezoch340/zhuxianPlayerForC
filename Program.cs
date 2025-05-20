using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Midi;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using System.Linq;

class Program
{
    // Windows API 函数声明
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // Windows 消息常量
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    private static readonly Dictionary<string, object> globalVariable = new Dictionary<string, object>
    {
        { "api_base", "http://150.109.34.78:88" },
        { "app_name", "zhuxianshijie_tanqin" },
        { "version", 0.05 },
        { "api_key", "f01b1825e75946c0bd22b23ec9361729" },
    };

    static async Task Main(string[] args)
    {
        //OpenBilibiliPage();
        var versionInfo = await CheckVersion();
        if (!versionInfo.is_enabled)
        {
            return;
        }

        if (versionInfo.update)
        {
            var localinfo = versionInfo.update_declaration.Replace("\\n", Environment.NewLine).Replace("\\", "");
            Console.WriteLine($"更新公告: {localinfo}");
            Console.WriteLine("下载链接: " + versionInfo.download_url);
            Console.WriteLine("是否立即更新？(Y/N): ");

            string userInput = Console.ReadLine()?.Trim().ToUpper();
            if (userInput == "Y")
            {
                OpenBrowser(versionInfo.download_url);
                Console.WriteLine("更新完成，程序退出。");
            }
            else
            {
                Console.WriteLine("用户取消更新，程序退出。");
            }
            return;
        }

        // 设置控制台标题和本地版本信息
        string localPrompt = $"LocalVersion: {globalVariable["version"]} 启动次数: {versionInfo.start_count}| {versionInfo.version_information}";
        Console.Title = localPrompt;

        // 处理多行换行符并打印版本声明
        string versionDeclaration = versionInfo.version_declaration.Replace("\\n", Environment.NewLine).Replace("\\", "");
        Console.WriteLine($"{localPrompt}\n");
        Console.WriteLine(versionDeclaration);

        // 提示程序已加载
        Console.WriteLine("自动演奏单人版本 已加载。");

        // 查找游戏窗口
        IntPtr gameWindowHandle = FindGameWindow();
        if (gameWindowHandle == IntPtr.Zero)
        {

            Console.WriteLine("无法找到游戏窗口，任意键退出");
            string userInput = Console.ReadLine()?.Trim().ToUpper();
            if (userInput == "Y")
            {
                return;
            }
            return;


        }

        Console.WriteLine("成功定位到游戏窗口！");

        // 提供 MIDI 输入设备选择
        int selectedDevice = SelectMidiDevice();
        if (selectedDevice == -1)
        {
            Console.WriteLine("未选择 MIDI 输入设备，程序退出。");
            return;
        }

        // 开始 MIDI 输入监听
        MidiHandler.Start(gameWindowHandle, selectedDevice);
    }

    public static IntPtr FindGameWindow()
    {
        Process[] processes = Process.GetProcessesByName("CoreKeeper");
        if (processes.Length > 0)
        {
            return processes[0].MainWindowHandle;
        }

        Console.WriteLine("未找到游戏窗口，请确认游戏是否已启动。");
        return IntPtr.Zero;
    }

    public static int SelectMidiDevice()
    {
        Console.WriteLine("可用的 MIDI 输入设备：");
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
        {
            string deviceName = MidiIn.DeviceInfo(i).ProductName;
            Console.WriteLine($"{i}: {deviceName}");
        }

        int selectedDevice = -1;
        while (selectedDevice < 0 || selectedDevice >= MidiIn.NumberOfDevices)
        {
            Console.Write("请输入设备编号：");
            if (int.TryParse(Console.ReadLine(), out selectedDevice) && selectedDevice >= 0 && selectedDevice < MidiIn.NumberOfDevices)
            {
                return selectedDevice;
            }
            else
            {
                Console.WriteLine("无效的设备编号，请重新选择。");
            }
        }

        return -1;
    }

    public static void SendKeyToGame(IntPtr gameWindowHandle, VirtualKeyCode key, bool keyDown)
    {
        uint msg = keyDown ? WM_KEYDOWN : WM_KEYUP;
        PostMessage(gameWindowHandle, msg, (IntPtr)key, IntPtr.Zero);
    }

    private static async Task<dynamic> CheckVersion()
    {
        string apiBase = globalVariable["api_base"].ToString();
        string appName = globalVariable["app_name"].ToString();
        string apiKey = globalVariable.ContainsKey("api_key") ? globalVariable["api_key"].ToString() : string.Empty;

        try
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true
            };
            using HttpClient client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("accept", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
            {
                client.DefaultRequestHeaders.Add("x_key", apiKey);
            }

            var response = await client.GetAsync($"{apiBase}/version/?appname={appName}");
            if (response.IsSuccessStatusCode)
            {
                string responseData = await response.Content.ReadAsStringAsync();

                var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, string>>(responseData);
                if (!jsonResponse.ContainsKey("code"))
                {
                    throw new Exception("Response does not contain 'code' field.");
                }

                string encryptedCode = jsonResponse["code"];

                // 解密并解析为 JsonElement
                var versionInfo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FromCode(encryptedCode));

                // 使用 JsonElement 的方法获取值
                bool isEnabled = versionInfo.ContainsKey("is_enabled") && versionInfo["is_enabled"].GetBoolean();
                bool updateAvailable = versionInfo.ContainsKey("version") &&
                       versionInfo["version"].ValueKind == JsonValueKind.Number &&
                       versionInfo["version"].GetDouble() > Convert.ToDouble(globalVariable["version"]);

                double version = versionInfo.ContainsKey("version") ? versionInfo["version"].GetDouble() : 0.0;
                int start_count = (int)(versionInfo.ContainsKey("start_count") ? versionInfo["start_count"].GetDouble() : 0);
                return new
                {
                    is_enabled = isEnabled,
                    update = updateAvailable,
                    version_information = versionInfo.ContainsKey("version_information") ? versionInfo["version_information"].GetString() : "无信息",
                    update_declaration = versionInfo.ContainsKey("update_declaration") ? versionInfo["update_declaration"].GetString() : "无信息",
                    version_declaration = versionInfo.ContainsKey("version_declaration") ? versionInfo["version_declaration"].GetString().Replace("\\n", Environment.NewLine) : "无信息",
                    download_url = versionInfo.ContainsKey("download_url") ? versionInfo["download_url"].GetString() : string.Empty,
                    version = version,
                    start_count = start_count,
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("版本检查失败: " + ex.Message);
        }

        return new
        {
            update = false,
            is_enabled = false,
            version_information = "无法链接到服务器,请检查网络连接",
            download_url = string.Empty,
            update_declaration = "无法链接到服务器,请检查网络连接",
            version_declaration = "无法链接到服务器,请检查网络连接",
            version = 0.0,
            start_count = 9999
        };
    }

    static void OpenBilibiliPage()
    {
        try
        {
            // 设置网址
            string url = "https://space.bilibili.com/35335853";

            // 使用默认浏览器打开该网址
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生错误: {ex.Message}");
        }
    }



    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            Console.WriteLine("浏览器已打开下载链接。");
        }
        catch (Exception ex)
        {
            Console.WriteLine("无法打开浏览器: " + ex.Message);
        }
    }

    private static string FromCode(string s)
    {
        string key = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        int cd = key.Length;
        List<int> resultChars = new List<int>();
        int d = 0;

        // 遍历加密字符串的每 3 个字符
        for (int i = 0; i < s.Length / 3; i++)
        {
            int b1 = key.IndexOf(s[d]);
            d++;
            int b2 = key.IndexOf(s[d]);
            d++;
            int b3 = key.IndexOf(s[d]);
            d++;

            int charCode = b1 * cd * cd + b2 * cd + b3;
            resultChars.Add(charCode);
        }

        var data = new string(resultChars.ConvertAll(code => (char)code).ToArray());

        // 修正为标准 JSON 格式
        data = data.Replace("'", "\"") // 替换单引号为双引号
                   .Replace("True", "true") // 修正布尔值
                   .Replace("False", "false");
        return data;
    }
}


class MidiHandler
{
    private static IntPtr gameWindowHandle;

    private static readonly Dictionary<int, Dictionary<int, bool>> activeNotesByChannel = new();

    public static void Start(IntPtr targetWindow, int selectedDevice)
    {
        gameWindowHandle = targetWindow;
        Console.WriteLine("开始监听 MIDI 输入... 按 Ctrl+C 退出。");

        var midiIn = new MidiIn(selectedDevice);
        midiIn.MessageReceived += OnMidiMessageReceived;
        midiIn.Start();

        Console.CancelKeyPress += (sender, e) =>
        {
            midiIn.Stop();
            Console.WriteLine("MIDI 输入监听已停止。");
        };

        while (true) { }
    }

    private static void OnMidiMessageReceived(object sender, MidiInMessageEventArgs e)
    {
        if (e.MidiEvent is NoteEvent noteEvent)
        {
            int noteNumber = noteEvent.NoteNumber;
            int velocity = noteEvent.Velocity;
            int channel = noteEvent.Channel;

            int mappedNote = 60 + (noteNumber % 24);

            bool isNoteOn = noteEvent.CommandCode == MidiCommandCode.NoteOn && velocity > 0;
            bool isNoteOff = noteEvent.CommandCode == MidiCommandCode.NoteOff ||
                             (noteEvent.CommandCode == MidiCommandCode.NoteOn && velocity == 0);

            if (!activeNotesByChannel.ContainsKey(channel))
                activeNotesByChannel[channel] = new Dictionary<int, bool>();

            var activeNotes = activeNotesByChannel[channel];

            if (isNoteOn)
            {
                Console.WriteLine($"NOTE ON: {noteNumber} => {mappedNote}, velocity: {velocity}");
                HandleNoteOn(channel, mappedNote, activeNotes);
            }
            else if (isNoteOff)
            {
                Console.WriteLine($"NOTE OFF: {noteNumber} => {mappedNote}, velocity: {velocity}");
                HandleNoteOff(channel, mappedNote, activeNotes);
            }
        }
    }

    private static void HandleNoteOn(int channel, int note, Dictionary<int, bool> activeNotes)
    {
        if (!midiToGuzheng.TryGetValue(note, out var key)) return;

        if (!activeNotes.ContainsKey(note) || !activeNotes[note])
        {
            activeNotes[note] = true;
            Console.WriteLine($"Key DOWN: {note} -> {key}");
            SendInputKey(key, true);
        }
    }

    private static void HandleNoteOff(int channel, int note, Dictionary<int, bool> activeNotes)
    {
        if (!midiToGuzheng.TryGetValue(note, out var key)) return;

        if (activeNotes.ContainsKey(note) && activeNotes[note])
        {
            activeNotes[note] = false;
            Console.WriteLine($"Key UP: {note} -> {key}");
            SendInputKey(key, false);
        }
    }

    private static void SendInputKey(VirtualKeyCode key, bool keyDown)
    {
        INPUT[] inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = (ushort)key;
        inputs[0].u.ki.wScan = 0;
        inputs[0].u.ki.dwFlags = keyDown ? 0u : KEYEVENTF_KEYUP;
        inputs[0].u.ki.time = 0;
        inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

        uint result = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (result == 0)
        {
            Console.WriteLine($"SendInput 失败: {Marshal.GetLastWin32Error()}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private static ushort VkToScanCode(ushort vk)
    {
        return (ushort)MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static readonly Dictionary<int, VirtualKeyCode> midiToGuzheng = new()
    {
        { 60, VirtualKeyCode.VK_Q }, { 61, VirtualKeyCode.VK_2 }, { 62, VirtualKeyCode.VK_W },
        { 63, VirtualKeyCode.VK_3 }, { 64, VirtualKeyCode.VK_E }, { 65, VirtualKeyCode.VK_R },
        { 66, VirtualKeyCode.VK_5 }, { 67, VirtualKeyCode.VK_T }, { 68, VirtualKeyCode.VK_6 },
        { 69, VirtualKeyCode.VK_Y }, { 70, VirtualKeyCode.VK_7 }, { 71, VirtualKeyCode.VK_U },
        { 72, VirtualKeyCode.VK_Z }, { 73, VirtualKeyCode.VK_S }, { 74, VirtualKeyCode.VK_X },
        { 75, VirtualKeyCode.VK_D }, { 76, VirtualKeyCode.VK_C }, { 77, VirtualKeyCode.VK_V },
        { 78, VirtualKeyCode.VK_G }, { 79, VirtualKeyCode.VK_B }, { 80, VirtualKeyCode.VK_H },
        { 81, VirtualKeyCode.VK_N }, { 82, VirtualKeyCode.VK_J }, { 83, VirtualKeyCode.VK_M },
    };
}

public enum VirtualKeyCode
{
    VK_Q = 0x51, VK_W = 0x57, VK_E = 0x45, VK_R = 0x52, VK_T = 0x54,
    VK_Y = 0x59, VK_U = 0x55, VK_Z = 0x5A, VK_X = 0x58, VK_C = 0x43,
    VK_V = 0x56, VK_B = 0x42, VK_N = 0x4E, VK_M = 0x4D,
    VK_2 = 0x32, VK_3 = 0x33, VK_5 = 0x35, VK_6 = 0x36, VK_7 = 0x37,
    VK_S = 0x53, VK_D = 0x44, VK_G = 0x47, VK_H = 0x48, VK_J = 0x4A,
}
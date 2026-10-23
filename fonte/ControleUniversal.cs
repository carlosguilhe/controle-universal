// Controle Universal - controla este PC pelo celular, pela mesma rede Wi-Fi.
// Compila com o csc.exe que ja vem no Windows (C# 5, .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ControleUniversal
{
    // ---------------------------------------------------------------- dados

    public class Site
    {
        public string id;
        public string titulo;
        public string url;
        public string cor;
    }

    public class Dispositivo
    {
        public string id;
        public string nome;
        public string hash;
        public string criado;
    }

    public class Dados
    {
        public string idPc;
        public int porta = 8765;
        public string navegador;
        public List<Site> sites = new List<Site>();
        public List<Dispositivo> dispositivos = new List<Dispositivo>();
        public string apelido;
        public string pastaRecebidos;
        public List<Cena> cenas = new List<Cena>();
        public List<Agenda> agenda = new List<Agenda>();
    }

    static class Config
    {
        public static readonly string Pasta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControleUniversal");
        static readonly string Arquivo = Path.Combine(Pasta, "config.json");
        static readonly object Trava = new object();
        public static Dados D = new Dados();

        public static void Carregar()
        {
            Directory.CreateDirectory(Pasta);
            try
            {
                if (File.Exists(Arquivo))
                    D = new JavaScriptSerializer().Deserialize<Dados>(File.ReadAllText(Arquivo, Encoding.UTF8)) ?? new Dados();
            }
            catch (Exception e) { Log("config invalida: " + e.Message); D = new Dados(); }
            bool mudou = false;
            if (string.IsNullOrEmpty(D.idPc)) { D.idPc = Guid.NewGuid().ToString("N"); mudou = true; }
            if (D.sites == null || D.sites.Count == 0) { D.sites = SitesPadrao(); mudou = true; }
            if (D.dispositivos == null) D.dispositivos = new List<Dispositivo>();
            if (D.cenas == null) D.cenas = new List<Cena>();
            if (D.agenda == null) D.agenda = new List<Agenda>();
            if (mudou) Salvar();
        }

        public static void Salvar()
        {
            lock (Trava)
            {
                File.WriteAllText(Arquivo, new JavaScriptSerializer().Serialize(D), Encoding.UTF8);
            }
        }

        static List<Site> SitesPadrao()
        {
            return new List<Site>
            {
                new Site { id = "youtube", titulo = "YouTube", url = "https://www.youtube.com/tv", cor = "#e62117" },
                new Site { id = "netflix", titulo = "Netflix", url = "https://www.netflix.com", cor = "#b20710" },
                new Site { id = "prime", titulo = "Prime Video", url = "https://www.primevideo.com", cor = "#1a98ff" },
                new Site { id = "globoplay", titulo = "Globoplay", url = "https://globoplay.globo.com", cor = "#ee3a2c" },
                new Site { id = "disney", titulo = "Disney+", url = "https://www.disneyplus.com", cor = "#113ccf" },
                new Site { id = "max", titulo = "Max", url = "https://www.max.com", cor = "#5822b4" },
                new Site { id = "twitch", titulo = "Twitch", url = "https://www.twitch.tv", cor = "#9146ff" },
                new Site { id = "spotify", titulo = "Spotify", url = "https://open.spotify.com", cor = "#1db954" }
            };
        }

        public static void Log(string s)
        {
            try { File.AppendAllText(Path.Combine(Pasta, "log.txt"), DateTime.Now.ToString("s") + " " + s + Environment.NewLine); }
            catch { }
        }
    }

    // ------------------------------------------------- entrada (mouse/teclado)

    static class Entrada
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct UNIAO { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public UNIAO u; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] entradas, int tamanho);

        const uint MOVE = 0x0001, ESQ_D = 0x0002, ESQ_U = 0x0004, DIR_D = 0x0008, DIR_U = 0x0010,
                   MEIO_D = 0x0020, MEIO_U = 0x0040, RODA = 0x0800;
        const uint TECLA_UP = 0x0002, TECLA_EXT = 0x0001, TECLA_UNI = 0x0004;

        static readonly object Trava = new object();

        static void Enviar(INPUT i)
        {
            lock (Trava) { SendInput(1, new INPUT[] { i }, Marshal.SizeOf(typeof(INPUT))); }
        }

        static void Mouse(int dx, int dy, uint dados, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 0;
            i.u.mi.dx = dx; i.u.mi.dy = dy; i.u.mi.mouseData = dados; i.u.mi.dwFlags = flags;
            Enviar(i);
        }

        static void Tecla(ushort vk, ushort scan, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 1;
            i.u.ki.wVk = vk; i.u.ki.wScan = scan; i.u.ki.dwFlags = flags;
            Enviar(i);
        }

        public static void Mover(int dx, int dy)
        {
            if (dx > 600) dx = 600; if (dx < -600) dx = -600;
            if (dy > 600) dy = 600; if (dy < -600) dy = -600;
            if (dx != 0 || dy != 0) Mouse(dx, dy, 0, MOVE);
        }

        // qual: l, r, m   acao: d (baixo), u (cima), c (clique)
        public static void Botao(char qual, char acao)
        {
            uint d, u;
            if (qual == 'r') { d = DIR_D; u = DIR_U; }
            else if (qual == 'm') { d = MEIO_D; u = MEIO_U; }
            else { d = ESQ_D; u = ESQ_U; }
            if (acao == 'd' || acao == 'c') Mouse(0, 0, 0, d);
            if (acao == 'u' || acao == 'c') Mouse(0, 0, 0, u);
        }

        public static void CtrlRoda(int delta)
        {
            Tecla(0x11, 0, 0);
            Roda(delta);
            Tecla(0x11, 0, TECLA_UP);
        }

        public static void Roda(int delta)
        {
            if (delta > 1200) delta = 1200; if (delta < -1200) delta = -1200;
            if (delta != 0) Mouse(0, 0, unchecked((uint)delta), RODA);
        }

        public static void Texto(string t)
        {
            if (t.Length > 2000) t = t.Substring(0, 2000);
            foreach (char c in t)
            {
                if (c == '\r') continue;
                if (c == '\n') { Pressionar(0x0D, true); continue; }
                Tecla(0, c, TECLA_UNI);
                Tecla(0, c, TECLA_UNI | TECLA_UP);
            }
        }

        static readonly Dictionary<string, ushort> Nomes = MontarNomes();
        static readonly HashSet<ushort> Estendidas = new HashSet<ushort> {
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B };

        static Dictionary<string, ushort> MontarNomes()
        {
            var d = new Dictionary<string, ushort>
            {
                {"enter", 0x0D}, {"esc", 0x1B}, {"backspace", 0x08}, {"tab", 0x09}, {"space", 0x20},
                {"left", 0x25}, {"up", 0x26}, {"right", 0x27}, {"down", 0x28},
                {"home", 0x24}, {"end", 0x23}, {"pgup", 0x21}, {"pgdn", 0x22},
                {"delete", 0x2E}, {"insert", 0x2D},
                {"ctrl", 0x11}, {"alt", 0x12}, {"shift", 0x10}, {"win", 0x5B},
                {"mute", 0xAD}, {"voldown", 0xAE}, {"volup", 0xAF},
                {"next", 0xB0}, {"prev", 0xB1}, {"stop", 0xB2}, {"playpause", 0xB3}
            };
            // pontuacao (teclado completo do celular, layout ABNT/US pelo codigo da tecla)
            d[";"] = 0xBA; d["="] = 0xBB; d[","] = 0xBC; d["-"] = 0xBD; d["."] = 0xBE; d["/"] = 0xBF;
            d["`"] = 0xC0; d["["] = 0xDB; d["\\"] = 0xDC; d["]"] = 0xDD; d["'"] = 0xDE;
            for (int i = 1; i <= 12; i++) d["f" + i] = (ushort)(0x6F + i);
            for (char c = 'a'; c <= 'z'; c++) d[c.ToString()] = (ushort)char.ToUpper(c);
            for (char c = '0'; c <= '9'; c++) d[c.ToString()] = (ushort)c;
            return d;
        }

        static void Pressionar(ushort vk, bool estendida)
        {
            uint ext = (estendida || Estendidas.Contains(vk)) ? TECLA_EXT : 0;
            Tecla(vk, 0, ext);
            Tecla(vk, 0, ext | TECLA_UP);
        }

        // "ctrl+w", "f11", "playpause"
        public static bool Combinacao(string combo)
        {
            var teclas = new List<ushort>();
            foreach (string parte in combo.ToLowerInvariant().Split('+'))
            {
                ushort vk;
                if (!Nomes.TryGetValue(parte.Trim(), out vk)) return false;
                teclas.Add(vk);
            }
            if (teclas.Count == 0) return false;
            foreach (ushort vk in teclas) Tecla(vk, 0, Estendidas.Contains(vk) ? TECLA_EXT : 0);
            for (int i = teclas.Count - 1; i >= 0; i--)
                Tecla(teclas[i], 0, TECLA_UP | (Estendidas.Contains(teclas[i]) ? TECLA_EXT : 0));
            return true;
        }
    }

    // ------------------------------------------------------------- navegador

    static class Navegador
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        // Navegadores encontrados neste PC (nome -> caminho), para o painel escolher.
        public static List<KeyValuePair<string, string>> Todos()
        {
            var r = new List<KeyValuePair<string, string>>();
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Action<string, string[]> procurar = delegate (string nome, string[] caminhos)
            {
                foreach (string c in caminhos) if (!string.IsNullOrEmpty(c) && File.Exists(c)) { r.Add(new KeyValuePair<string, string>(nome, c)); return; }
            };
            procurar("Brave", new string[] {
                pf == null ? null : Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                pf86 == null ? null : Path.Combine(pf86, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe"), RegistroApp("brave.exe") });
            procurar("Chrome", new string[] {
                pf == null ? null : Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                pf86 == null ? null : Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"), RegistroApp("chrome.exe") });
            procurar("Edge", new string[] { RegistroApp("msedge.exe"),
                pf86 == null ? null : Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe") });
            return r;
        }

        public static string Achar()
        {
            if (!string.IsNullOrEmpty(Config.D.navegador) && File.Exists(Config.D.navegador)) return Config.D.navegador;
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidatos = new List<string>();
            foreach (string baseDir in new string[] { pf, pf86, local })
                if (!string.IsNullOrEmpty(baseDir))
                    candidatos.Add(Path.Combine(baseDir, @"BraveSoftware\Brave-Browser\Application\brave.exe"));
            candidatos.Add(RegistroApp("brave.exe"));
            foreach (string baseDir in new string[] { pf, pf86, local })
                if (!string.IsNullOrEmpty(baseDir))
                    candidatos.Add(Path.Combine(baseDir, @"Google\Chrome\Application\chrome.exe"));
            candidatos.Add(RegistroApp("chrome.exe"));
            candidatos.Add(RegistroApp("msedge.exe"));
            foreach (string c in candidatos)
                if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
            return null;
        }

        static string RegistroApp(string exe)
        {
            foreach (RegistryKey raiz in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using (RegistryKey k = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                    {
                        if (k != null) { object v = k.GetValue(null); if (v != null) return v.ToString(); }
                    }
                }
                catch { }
            }
            return null;
        }

        public static bool UrlValida(string url)
        {
            Uri u;
            if (url == null || url.Length > 500) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != "http" && u.Scheme != "https") return false;
            return url.IndexOfAny(new char[] { ' ', '"', '\'', '\r', '\n', '<', '>', '^', '&', '|' }) < 0;
        }

        // Abre como janela sem barras ("app") e coloca em tela cheia.
        public static bool Abrir(string url)
        {
            string exe = Achar();
            if (exe == null || !UrlValida(url)) return false;
            var antes = JanelasNavegador();
            try
            {
                var psi = new ProcessStartInfo(exe, "--app=" + url) { UseShellExecute = false };
                Process.Start(psi);
            }
            catch (Exception e) { Config.Log("abrir: " + e.Message); return false; }
            new Thread(delegate () { TelaCheia(antes); }) { IsBackground = true }.Start();
            return true;
        }

        public static void AbrirNavegador()
        {
            string exe = Achar();
            if (exe == null) return;
            var antes = JanelasNavegador();
            try { Process.Start(new ProcessStartInfo(exe, "--new-window") { UseShellExecute = false }); } catch { }
            new Thread(delegate () { TelaCheia(antes); }) { IsBackground = true }.Start();
        }

        static bool ENavegador(string proc) { return proc == "brave" || proc == "chrome" || proc == "msedge"; }

        static HashSet<long> JanelasNavegador()
        {
            var conjunto = new HashSet<long>();
            try
            {
                foreach (var j in Sistema.Janelas())
                    if (ENavegador(((string)j["p"]).ToLowerInvariant())) conjunto.Add((long)j["h"]);
            }
            catch { }
            return conjunto;
        }

        // Acha a janela nova do navegador, traz para a frente (o Windows nao deixa programas em segundo plano
        // roubarem o foco, entao Sistema.Focar usa o truque da tecla Alt) e coloca em tela cheia.
        static void TelaCheia(HashSet<long> antes)
        {
            for (int tentativa = 0; tentativa < 14; tentativa++)
            {
                Thread.Sleep(600);
                try
                {
                    foreach (var j in Sistema.Janelas())
                    {
                        long h = (long)j["h"];
                        if (!ENavegador(((string)j["p"]).ToLowerInvariant()) || antes.Contains(h)) continue;
                        Sistema.Focar(h);
                        Thread.Sleep(500);
                        IntPtr ph = new IntPtr(h);
                        RECT r; GetWindowRect(ph, out r);
                        Rectangle tela = Screen.FromHandle(ph).Bounds;
                        bool cheia = r.Left <= tela.Left && r.Top <= tela.Top && r.Right >= tela.Right && r.Bottom >= tela.Bottom;
                        if (!cheia) Entrada.Combinacao("f11");
                        return;
                    }
                }
                catch { }
            }
        }
    }

    // ------------------------------------------- volume do Windows (CoreAudio)

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        int f0(); int f1(); int f2();
        int SetMasterVolumeLevel(float a, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float a, ref Guid ctx);
        int GetMasterVolumeLevel(out float a);
        int GetMasterVolumeLevelScalar(out float a);
        int f3(); int f4(); int f5(); int f6();
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid id, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        int OpenPropertyStore(int acesso, out IPropertyStore ps);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int estado);
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator { int EnumAudioEndpoints(int fluxo, int estados, out IMMDeviceCollection col); int GetDefaultAudioEndpoint(int a, int b, out IMMDevice d); }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    static class Volume
    {
        static IAudioEndpointVolume Ponto()
        {
            var e = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice d; e.GetDefaultAudioEndpoint(0, 1, out d);
            object o; Guid g = typeof(IAudioEndpointVolume).GUID;
            d.Activate(ref g, 23, IntPtr.Zero, out o);
            return (IAudioEndpointVolume)o;
        }
        public static float Ler() { float v; Ponto().GetMasterVolumeLevelScalar(out v); return v; }
        public static bool Mudo() { bool m; Ponto().GetMute(out m); return m; }
        public static void Definir(float v)
        {
            if (v < 0) v = 0; if (v > 1) v = 1;
            Guid g = Guid.Empty; Ponto().SetMasterVolumeLevelScalar(v, ref g);
        }
    }

    // ---------------------------------- sistema (janelas, energia, tela, area de transferencia)

    static class Sistema
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
        [DllImport("user32.dll")] static extern bool LockWorkStation();
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernar, bool forcar, bool acordar);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);

        // ---- mouse absoluto (0..10000 em cada eixo)
        static Screen Monitor(int i)
        {
            Screen[] todos = Screen.AllScreens;
            return (i >= 0 && i < todos.Length) ? todos[i] : Screen.PrimaryScreen;
        }

        public static List<Dictionary<string, object>> Monitores()
        {
            var lista = new List<Dictionary<string, object>>();
            Screen[] todos = Screen.AllScreens;
            for (int i = 0; i < todos.Length; i++)
                lista.Add(new Dictionary<string, object> { { "i", i }, { "w", todos[i].Bounds.Width }, { "h", todos[i].Bounds.Height }, { "p", todos[i].Primary } });
            return lista;
        }

        public static void MoverAbs(int nx, int ny) { MoverAbs(nx, ny, -1); }

        public static void MoverAbs(int nx, int ny, int monitor)
        {
            nx = Math.Max(0, Math.Min(10000, nx)); ny = Math.Max(0, Math.Min(10000, ny));
            Rectangle b = Monitor(monitor).Bounds;
            SetCursorPos(b.X + (int)((long)nx * (b.Width - 1) / 10000), b.Y + (int)((long)ny * (b.Height - 1) / 10000));
        }

        // ---- janelas
        public static List<Dictionary<string, object>> Janelas()
        {
            var lista = new List<Dictionary<string, object>>();
            IntPtr frente = GetForegroundWindow();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                int n = GetWindowTextLength(h); if (n == 0) return true;
                int ex = GetWindowLong(h, -20);
                bool ferramenta = (ex & 0x80) != 0, app = (ex & 0x40000) != 0;
                if (ferramenta) return true;
                if (GetWindow(h, 4) != IntPtr.Zero && !app) return true;
                int oculta = 0; DwmGetWindowAttribute(h, 14, out oculta, 4); if (oculta != 0) return true;
                var sb = new StringBuilder(n + 1); GetWindowText(h, sb, n + 1);
                string titulo = sb.ToString(); if (titulo == "Program Manager") return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                string proc = ""; try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }
                lista.Add(new Dictionary<string, object> {
                    {"h", (long)h}, {"t", titulo}, {"p", proc}, {"a", h == frente}, {"m", IsIconic(h)} });
                return lista.Count < 60;
            }, IntPtr.Zero);
            return lista;
        }
        public static void Focar(long hl)
        {
            IntPtr h = new IntPtr(hl);
            if (IsIconic(h)) ShowWindow(h, 9);
            Entrada.Combinacao("alt");          // libera o Windows a trocar o foco
            SetForegroundWindow(h);
        }
        public static void Minimizar(long hl) { ShowWindow(new IntPtr(hl), 6); }
        public static void Fechar(long hl) { PostMessage(new IntPtr(hl), 0x0010, IntPtr.Zero, IntPtr.Zero); }

        // ---- energia
        static DateTime? prazo;
        static void Exec(string exe, string args)
        {
            try { Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }
        public static void Energia(string acao)
        {
            switch (acao)
            {
                case "shutdown": Exec("shutdown", "/s /t 5"); break;
                case "restart": Exec("shutdown", "/r /t 5"); break;
                case "cancel": Exec("shutdown", "/a"); prazo = null; break;
                case "lock": LockWorkStation(); break;
                case "sleep": new Thread(delegate () { SetSuspendState(false, false, false); }) { IsBackground = true }.Start(); break;
                case "screenoff": PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2)); break;
            }
        }
        public static void Temporizador(int minutos)
        {
            Exec("shutdown", "/a");
            if (minutos <= 0) { prazo = null; return; }
            if (minutos > 720) minutos = 720;
            Exec("shutdown", "/s /t " + (minutos * 60));
            prazo = DateTime.Now.AddMinutes(minutos);
        }
        public static int Restante()
        {
            if (prazo == null) return 0;
            int s = (int)(prazo.Value - DateTime.Now).TotalSeconds;
            if (s <= 0) { prazo = null; return 0; }
            return s;
        }

        // ---- area de transferencia (precisa de thread STA)
        public static string LerClip()
        {
            string r = "";
            var t = new Thread(delegate () { try { if (Clipboard.ContainsText()) r = Clipboard.GetText(); } catch { } });
            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join(3000);
            return r;
        }
        public static void EscreverClip(string texto)
        {
            if (texto.Length > 100000) texto = texto.Substring(0, 100000);
            var t = new Thread(delegate () { try { if (texto.Length == 0) Clipboard.Clear(); else Clipboard.SetText(texto); } catch { } });
            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join(3000);
        }

        // ---- captura de tela (JPEG) com o ponteiro do mouse desenhado
        static ImageCodecInfo codecJpeg;
        public static byte[] Captura(int largura, long qualidade, int monitor)
        {
            if (largura < 200) largura = 200; if (largura > 1920) largura = 1920;
            Rectangle b = Monitor(monitor).Bounds;
            using (var cheia = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(cheia))
                {
                    g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    Point p; if (GetCursorPos(out p))
                    {
                        int x = p.X - b.X, y = p.Y - b.Y, raio = Math.Max(8, b.Width / 130);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        using (var pena = new Pen(Color.FromArgb(232, 89, 12), Math.Max(3, raio / 3)))
                            g.DrawEllipse(pena, x - raio, y - raio, raio * 2, raio * 2);
                    }
                }
                int altura = (int)((long)b.Height * largura / b.Width);
                using (var peq = new Bitmap(largura, altura, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g2 = Graphics.FromImage(peq))
                    {
                        g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        g2.DrawImage(cheia, 0, 0, largura, altura);
                    }
                    if (codecJpeg == null)
                        foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") codecJpeg = c;
                    var ps = new EncoderParameters(1);
                    ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, qualidade);
                    using (var ms = new MemoryStream()) { peq.Save(ms, codecJpeg, ps); return ms.ToArray(); }
                }
            }
        }
    }

    // ------------------------------------- "tocando agora" (Windows Media Controls)

    static class Midia
    {
        static string cache = "{}";
        static DateTime quando = DateTime.MinValue;
        static readonly object trava = new object();
        const string Script = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Esperar($op, $tipo) { $m = $asTask.MakeGenericMethod($tipo); $t = $m.Invoke($null, @($op)); [void]$t.Wait(-1); $t.Result }
[void][Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
$gm = Esperar ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$s = $gm.GetCurrentSession()
if (-not $s) { '{}'; exit }
$p = Esperar ($s.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
$i = $s.GetPlaybackInfo(); $t = $s.GetTimelineProperties()
@{ titulo = [string]$p.Title; artista = [string]$p.Artist; app = [string]$s.SourceAppUserModelId; estado = [int]$i.PlaybackStatus; pos = [int]$t.Position.TotalSeconds; fim = [int]$t.EndTime.TotalSeconds } | ConvertTo-Json -Compress
";
        public static string Agora()
        {
            lock (trava)
            {
                if ((DateTime.Now - quando).TotalSeconds < 2.5) return cache;
                string r = "{}";
                try
                {
                    string cod = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
                    var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + cod)
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                    using (Process p = Process.Start(psi))
                    {
                        string saida = p.StandardOutput.ReadToEnd();
                        if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } }
                        saida = saida.Trim();
                        if (saida.StartsWith("{")) r = saida;
                    }
                }
                catch (Exception e) { Config.Log("midia: " + e.Message); }
                cache = r; quando = DateTime.Now;
                return r;
            }
        }
    }

    // ------------------------------------------------------- canal de resposta (WebSocket)

    class Canal
    {
        public Stream S;
        readonly object trava = new object();
        public void Enviar(string tipo, string json)
        {
            try { lock (trava) { Servidor.EnviarTexto(S, tipo + " " + json); } } catch { }
        }
        public void Pong() { lock (trava) { Servidor.EnviarFrame(S, 10, ""); } }
    }

    // --------------------------------------------------------------- servidor

    class Requisicao
    {
        public string Metodo, Caminho, Corpo = "";
        public long TamanhoCorpo;      // so para envio de arquivos: o corpo fica no fluxo, para ser lido aos poucos
        public Dictionary<string, string> Query = new Dictionary<string, string>();
        public Dictionary<string, string> Cab = new Dictionary<string, string>();
        public bool Local;
        public string Cabecalho(string nome) { string v; return Cab.TryGetValue(nome.ToLowerInvariant(), out v) ? v : ""; }
    }

    class Servidor
    {
        TcpListener ouvinte;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        string codigo;
        int falhas;
        DateTime bloqueadoAte = DateTime.MinValue;
        readonly object trava = new object();
        static readonly Random Aleatorio = new Random();

        public string Codigo { get { lock (trava) { return codigo; } } }

        public void NovoCodigo()
        {
            var b = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            lock (trava) { codigo = (BitConverter.ToUInt32(b, 0) % 1000000).ToString("D6"); }
        }

        public void Iniciar()
        {
            NovoCodigo();
            ouvinte = new TcpListener(IPAddress.Any, Config.D.porta);
            ouvinte.Start();
            new Thread(Laco) { IsBackground = true }.Start();
            Agendador.Iniciar();
        }

        // Nome mostrado no celular: o apelido escolhido no painel, ou o nome do Windows.
        public static string NomePc()
        {
            return string.IsNullOrWhiteSpace(Config.D.apelido) ? Environment.MachineName : Config.D.apelido.Trim();
        }

        // Celulares com o controle aberto agora (id do dispositivo -> conexoes) e ultimo uso.
        static readonly Dictionary<string, int> Online = new Dictionary<string, int>();
        static readonly Dictionary<string, DateTime> UltimoUso = new Dictionary<string, DateTime>();
        static string IdDispositivo(Requisicao r)
        {
            string token = r.Cabecalho("x-token");
            if (token == "" && r.Query.ContainsKey("t")) token = r.Query["t"];
            if (token == "") return null;
            string h = Hash(token);
            lock (Config.D) { Dispositivo d = Config.D.dispositivos.FirstOrDefault(x => x.hash == h); return d == null ? null : d.id; }
        }
        static void Marcar(string id, int delta)
        {
            if (id == null) return;
            lock (Online)
            {
                int n; Online.TryGetValue(id, out n); n += delta; if (n < 0) n = 0; Online[id] = n; UltimoUso[id] = DateTime.Now;
            }
        }

        public void Parar() { try { ouvinte.Stop(); } catch { } }

        void Laco()
        {
            while (true)
            {
                TcpClient c;
                try { c = ouvinte.AcceptTcpClient(); } catch { return; }
                new Thread(delegate () { Atender(c); }) { IsBackground = true }.Start();
            }
        }

        void Atender(TcpClient c)
        {
            try
            {
                using (c)
                {
                    c.NoDelay = true;
                    c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    NetworkStream s = c.GetStream();
                    s.ReadTimeout = 8000; s.WriteTimeout = 8000;
                    Requisicao r = LerRequisicao(s, c);
                    if (r != null) Rotear(s, r);
                }
            }
            catch (Exception e) { if (!(e is IOException)) Config.Log("atender: " + e.Message); }
        }

        static Requisicao LerRequisicao(NetworkStream s, TcpClient c)
        {
            var buf = new List<byte>();
            int ultimos = 0, b;
            while (buf.Count < 16384)
            {
                b = s.ReadByte();
                if (b < 0) return null;
                buf.Add((byte)b);
                ultimos = ((ultimos << 8) | b) & 0x7FFFFFFF;
                int n = buf.Count;
                if (n >= 4 && buf[n - 4] == 13 && buf[n - 3] == 10 && buf[n - 2] == 13 && buf[n - 1] == 10) break;
            }
            string[] linhas = Encoding.ASCII.GetString(buf.ToArray()).Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] p = linhas[0].Split(' ');
            if (p.Length < 2) return null;
            var r = new Requisicao { Metodo = p[0].ToUpperInvariant() };
            string alvo = p[1];
            int q = alvo.IndexOf('?');
            r.Caminho = q >= 0 ? alvo.Substring(0, q) : alvo;
            if (q >= 0)
                foreach (string par in alvo.Substring(q + 1).Split('&'))
                {
                    int e = par.IndexOf('=');
                    if (e > 0) r.Query[Uri.UnescapeDataString(par.Substring(0, e))] = Uri.UnescapeDataString(par.Substring(e + 1).Replace('+', ' '));
                }
            for (int i = 1; i < linhas.Length; i++)
            {
                int d = linhas[i].IndexOf(':');
                if (d > 0) r.Cab[linhas[i].Substring(0, d).Trim().ToLowerInvariant()] = linhas[i].Substring(d + 1).Trim();
            }
            IPEndPoint remoto = c.Client.RemoteEndPoint as IPEndPoint;
            r.Local = remoto != null && IPAddress.IsLoopback(remoto.Address);
            long tamL;
            if (long.TryParse(r.Cabecalho("content-length"), out tamL) && tamL > 0)
            {
                if (r.Metodo == "POST" && r.Caminho == "/api/arquivo") { r.TamanhoCorpo = tamL; return r; }
                if (tamL > 65536) return null;
                int tam = (int)tamL;
                byte[] corpo = new byte[tam];
                int lido = 0;
                while (lido < tam) { int k = s.Read(corpo, lido, tam - lido); if (k <= 0) return null; lido += k; }
                r.Corpo = Encoding.UTF8.GetString(corpo);
            }
            return r;
        }

        // ---------- respostas

        static void Responder(Stream s, int status, string tipo, byte[] corpo, bool cors)
        {
            string texto = status == 200 ? "OK" : status == 101 ? "Switching Protocols" : status == 204 ? "No Content"
                : status == 401 ? "Unauthorized" : status == 403 ? "Forbidden" : status == 404 ? "Not Found"
                : status == 429 ? "Too Many Requests" : "Bad Request";
            var h = new StringBuilder();
            h.Append("HTTP/1.1 ").Append(status).Append(' ').Append(texto).Append("\r\n");
            h.Append("Content-Type: ").Append(tipo).Append("\r\n");
            h.Append("Content-Length: ").Append(corpo.Length).Append("\r\n");
            h.Append("Cache-Control: no-store\r\nConnection: close\r\n");
            h.Append("X-Content-Type-Options: nosniff\r\n");
            if (cors)
                h.Append("Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: X-Token, Content-Type\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            h.Append("\r\n");
            byte[] cab = Encoding.ASCII.GetBytes(h.ToString());
            s.Write(cab, 0, cab.Length);
            if (corpo.Length > 0) s.Write(corpo, 0, corpo.Length);
            s.Flush();
        }

        static void RespJson(Stream s, int status, object o, bool cors)
        {
            Responder(s, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(Json.Serialize(o)), cors);
        }

        // Manda um arquivo do PC em pedacos (sem carregar tudo na memoria).
        static void EnviarArquivo(Stream s, string caminho)
        {
            var fi = new FileInfo(caminho);
            if (!fi.Exists) { Responder(s, 404, "text/plain", new byte[0], true); return; }
            string nome = Uri.EscapeDataString(fi.Name);
            string cab = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + fi.Length
                + "\r\nContent-Disposition: attachment; filename*=UTF-8''" + nome + "\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\n\r\n";
            byte[] cb = Encoding.ASCII.GetBytes(cab);
            s.WriteTimeout = 60000;
            s.Write(cb, 0, cb.Length);
            using (FileStream f = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buf = new byte[64 * 1024]; int k;
                while ((k = f.Read(buf, 0, buf.Length)) > 0) s.Write(buf, 0, k);
            }
            s.Flush();
        }

        // Recebe um arquivo do celular (corpo da requisicao) e grava na pasta de recebidos.
        static void ReceberArquivo(NetworkStream s, Requisicao r)
        {
            string nome = Arquivos.NomeSeguro(r.Query.ContainsKey("n") ? r.Query["n"] : "arquivo");
            string alvo = Arquivos.NomeUnico(Arquivos.PastaRecebidos(), nome);
            s.ReadTimeout = 30000;
            long falta = r.TamanhoCorpo;
            try
            {
                using (FileStream f = new FileStream(alvo, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[64 * 1024];
                    while (falta > 0)
                    {
                        int k = s.Read(buf, 0, (int)Math.Min(buf.Length, falta));
                        if (k <= 0) throw new IOException("envio interrompido");
                        f.Write(buf, 0, k); falta -= k;
                    }
                }
            }
            catch { try { File.Delete(alvo); } catch { } throw; }
            if (r.Query.ContainsKey("abrir") && r.Query["abrir"] == "1") { try { Arquivos.Abrir(alvo); } catch { } }
            RespJson(s, 200, new Dictionary<string, object> { { "ok", true }, { "caminho", alvo }, { "nome", Path.GetFileName(alvo) } }, true);
        }

        static byte[] Recurso(string nome)
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(nome))
            {
                if (st == null) return null;
                var ms = new MemoryStream();
                st.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static void Arquivo(Stream s, string recurso, string tipo)
        {
            byte[] b = Recurso(recurso);
            if (b == null) Responder(s, 404, "text/plain", new byte[0], false);
            else Responder(s, 200, tipo, b, false);
        }

        // ---------- autenticacao

        static string Hash(string token)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "").ToLowerInvariant();
        }

        static bool Autorizado(Requisicao r)
        {
            string token = r.Cabecalho("x-token");
            if (token == "" && r.Query.ContainsKey("t")) token = r.Query["t"];
            if (token == "" || token.Length > 100) return false;
            string h = Hash(token);
            lock (Config.D) { return Config.D.dispositivos.Any(d => d.hash == h); }
        }

        static bool HostLocal(Requisicao r)
        {
            string host = r.Cabecalho("host").ToLowerInvariant();
            return r.Local && (host.StartsWith("localhost:") || host.StartsWith("127.0.0.1:") || host == "localhost" || host == "127.0.0.1");
        }

        static Dictionary<string, object> LerJson(string corpo)
        {
            try { return Json.Deserialize<Dictionary<string, object>>(corpo) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        static string Texto(Dictionary<string, object> d, string chave)
        {
            object v; return d.TryGetValue(chave, out v) && v != null ? v.ToString() : "";
        }

        // ---------- rotas

        void Rotear(NetworkStream s, Requisicao r)
        {
            string c = r.Caminho;
            if (r.Metodo == "OPTIONS")
            {
                if (c.StartsWith("/api/painel")) { Responder(s, 403, "text/plain", new byte[0], false); return; }
                Responder(s, 204, "text/plain", new byte[0], true); return;
            }

            if (r.Metodo == "GET" && c == "/") { Arquivo(s, "app.html", "text/html; charset=utf-8"); return; }
            if (r.Metodo == "GET" && c == "/manifest.json")
            {
                RespJson(s, 200, new Dictionary<string, object> {
                    {"name", "Controle Universal"}, {"short_name", "Controle"}, {"start_url", "/"},
                    {"display", "standalone"}, {"background_color", "#0e1116"}, {"theme_color", "#0e1116"} }, false);
                return;
            }
            if (r.Metodo == "GET" && c == "/painel")
            {
                if (!HostLocal(r)) { Responder(s, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("O painel so abre no proprio PC."), false); return; }
                Arquivo(s, "painel.html", "text/html; charset=utf-8"); return;
            }
            if (r.Metodo == "GET" && c == "/qrcode.js") { Arquivo(s, "qrcode.js", "application/javascript; charset=utf-8"); return; }

            if (r.Metodo == "GET" && c == "/api/info")
            {
                RespJson(s, 200, new Dictionary<string, object> {
                    {"app", "controle-universal"}, {"id", Config.D.idPc}, {"nome", NomePc()},
                    {"porta", Config.D.porta}, {"ips", IpsLan()}, {"mac", MacLan()} }, true);
                return;
            }
            if (r.Metodo == "GET" && c == "/app.apk")
            {
                byte[] apk = Recurso("app.apk");
                if (apk == null) { Responder(s, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Este programa foi montado sem o aplicativo do celular."), false); return; }
                Responder(s, 200, "application/vnd.android.package-archive", apk, false); return;
            }
            if (r.Metodo == "GET" && c == "/api/icone")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                string host = Icones.HostDe(r.Query.ContainsKey("u") ? r.Query["u"] : "");
                string tipoIcone; byte[] icone = host == null ? null : Icones.Obter(host, out tipoIcone);
                if (host == null) { Responder(s, 400, "text/plain", new byte[0], true); return; }
                tipoIcone = Icones.Tipo(icone);
                if (icone == null || tipoIcone == null) { Responder(s, 404, "text/plain", new byte[0], true); return; }
                string cab = "HTTP/1.1 200 OK\r\nContent-Type: " + tipoIcone + "\r\nContent-Length: " + icone.Length
                    + "\r\nCache-Control: private, max-age=86400\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\n\r\n";
                byte[] cb = Encoding.ASCII.GetBytes(cab); s.Write(cb, 0, cb.Length); s.Write(icone, 0, icone.Length); s.Flush();
                return;
            }
            if (r.Metodo == "GET" && c == "/api/tela")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                int larg = 720, qual = 45, mon = 0;
                if (r.Query.ContainsKey("w")) int.TryParse(r.Query["w"], out larg);
                if (r.Query.ContainsKey("q")) int.TryParse(r.Query["q"], out qual);
                if (r.Query.ContainsKey("m")) int.TryParse(r.Query["m"], out mon);
                try { Responder(s, 200, "image/jpeg", Sistema.Captura(larg, Math.Max(15, Math.Min(90, qual)), mon), true); }
                catch (Exception e) { Config.Log("captura: " + e.Message); Responder(s, 400, "text/plain", new byte[0], true); }
                return;
            }
            if (r.Metodo == "POST" && c == "/api/cmd")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                Comando(r.Corpo.Trim(), null);
                RespJson(s, 200, new Dictionary<string, object> { { "ok", true } }, true);
                return;
            }
            if (r.Metodo == "POST" && c == "/api/sites")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                int total = SalvarSites(r.Corpo);
                if (total < 0) RespJson(s, 400, new Dictionary<string, object> { { "erro", "lista invalida" } }, true);
                else RespJson(s, 200, new Dictionary<string, object> { { "ok", true }, { "total", total } }, true);
                return;
            }
            if (c == "/api/cenas" || c == "/api/agenda")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                bool cena = c == "/api/cenas";
                if (r.Metodo == "POST")
                {
                    int t = cena ? Cenas.Salvar(r.Corpo) : Agendador.Salvar(r.Corpo);
                    if (t < 0) { RespJson(s, 400, new Dictionary<string, object> { { "erro", "dados invalidos" } }, true); return; }
                }
                object lista; lock (Config.D) { lista = cena ? (object)new List<Cena>(Config.D.cenas) : (object)new List<Agenda>(Config.D.agenda); }
                RespJson(s, 200, new Dictionary<string, object> { { cena ? "cenas" : "itens", lista } }, true);
                return;
            }
            if (r.Metodo == "GET" && c == "/api/pasta")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                try { RespJson(s, 200, Arquivos.Listar(r.Query.ContainsKey("c") ? r.Query["c"] : "@"), true); }
                catch { RespJson(s, 404, new Dictionary<string, object> { { "erro", "pasta nao encontrada" } }, true); }
                return;
            }
            if (r.Metodo == "GET" && c == "/api/baixar")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                try { EnviarArquivo(s, Path.GetFullPath(r.Query.ContainsKey("c") ? r.Query["c"] : "")); }
                catch (IOException) { throw; }
                catch (Exception e) { Config.Log("baixar: " + e.Message); Responder(s, 404, "text/plain", new byte[0], true); }
                return;
            }
            if (r.Metodo == "POST" && c == "/api/arquivo")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                ReceberArquivo(s, r);
                return;
            }
            if (r.Metodo == "POST" && c == "/api/pair") { Parear(s, r); return; }

            if (c.StartsWith("/api/painel/"))
            {
                if (!HostLocal(r) || r.Cabecalho("x-painel") != "1") { Responder(s, 403, "text/plain", new byte[0], false); return; }
                Painel(s, r); return;
            }

            if (r.Metodo == "GET" && c == "/api/sites")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                List<Site> copia; lock (Config.D) { copia = new List<Site>(Config.D.sites); }
                RespJson(s, 200, new Dictionary<string, object> { { "sites", copia }, { "navegador", NomeNavegador() } }, true);
                return;
            }
            if (r.Metodo == "GET" && c == "/ws")
            {
                if (!Autorizado(r)) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "nao autorizado" } }, true); return; }
                WebSocket(s, r); return;
            }
            Responder(s, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("nao encontrado"), false);
        }

        void Parear(NetworkStream s, Requisicao r)
        {
            lock (trava)
            {
                if (DateTime.Now < bloqueadoAte)
                {
                    RespJson(s, 429, new Dictionary<string, object> { { "erro", "Muitas tentativas. Espere um minuto." } }, true); return;
                }
            }
            var d = LerJson(r.Corpo);
            string informado = Texto(d, "codigo").Trim();
            bool ok;
            lock (trava)
            {
                ok = informado.Length == 6 && informado == codigo;
                if (!ok && ++falhas >= 5) { bloqueadoAte = DateTime.Now.AddMinutes(1); falhas = 0; }
                if (ok) falhas = 0;
            }
            if (!ok) { RespJson(s, 401, new Dictionary<string, object> { { "erro", "Codigo incorreto." } }, true); return; }

            var b = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            string token = Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            string nome = Texto(d, "nome"); if (nome.Length > 40) nome = nome.Substring(0, 40); if (nome == "") nome = "Celular";
            lock (Config.D)
            {
                Config.D.dispositivos.Add(new Dispositivo {
                    id = Guid.NewGuid().ToString("N").Substring(0, 8), nome = nome, hash = Hash(token), criado = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
                Config.Salvar();
            }
            NovoCodigo();
            RespJson(s, 200, new Dictionary<string, object> { { "token", token }, { "nome", Environment.MachineName } }, true);
        }

        void Painel(NetworkStream s, Requisicao r)
        {
            string c = r.Caminho;
            if (c == "/api/painel/estado")
            {
                var urls = new List<string>();
                urls.Add("http://" + Environment.MachineName.ToLowerInvariant() + ".local:" + Config.D.porta + "/");
                foreach (string ip in IpsLan()) urls.Add("http://" + ip + ":" + Config.D.porta + "/");
                List<Site> sites; List<Dispositivo> devs;
                lock (Config.D) { sites = new List<Site>(Config.D.sites); devs = new List<Dispositivo>(Config.D.dispositivos); }
                var apks = new List<string>();
                if (Recurso("app.apk") != null) foreach (string ip in IpsLan()) apks.Add("http://" + ip + ":" + Config.D.porta + "/app.apk");
                var navs = new List<Dictionary<string, object>>();
                foreach (var kv in Navegador.Todos()) navs.Add(new Dictionary<string, object> { { "nome", kv.Key }, { "caminho", kv.Value } });
                RespJson(s, 200, new Dictionary<string, object> {
                    {"nome", NomePc()}, {"maquina", Environment.MachineName}, {"apelido", Config.D.apelido ?? ""},
                    {"codigo", Codigo}, {"urls", urls}, {"sites", sites}, {"apks", apks},
                    {"navegador", NomeNavegador()}, {"navegadorConfig", Config.D.navegador ?? ""}, {"navegadores", navs},
                    {"recebidos", Arquivos.PastaRecebidos()}, {"instalado", Instalador.RodandoDaInstalacao()},
                    {"dispositivos", devs.Select(x => {
                        int on; DateTime vis; bool tem;
                        lock (Online) { Online.TryGetValue(x.id, out on); tem = UltimoUso.TryGetValue(x.id, out vis); if (!tem) vis = DateTime.MinValue; }
                        return new Dictionary<string, object> { {"id", x.id}, {"nome", x.nome}, {"criado", x.criado}, {"online", on > 0}, {"visto", tem ? vis.ToString("dd/MM HH:mm") : ""} };
                    }).ToList()} }, false);
                return;
            }
            if (r.Metodo != "POST") { Responder(s, 400, "text/plain", new byte[0], false); return; }
            if (c == "/api/painel/config")
            {
                var d = LerJson(r.Corpo);
                string apelido = Texto(d, "apelido").Trim(), nav = Texto(d, "navegador").Trim(), pasta = Texto(d, "recebidos").Trim();
                if (apelido.Length > 40) apelido = apelido.Substring(0, 40);
                if (nav.Length > 0 && !File.Exists(nav)) { RespJson(s, 400, new Dictionary<string, object> { { "erro", "Navegador nao encontrado." } }, false); return; }
                if (pasta.Length > 0) { try { Directory.CreateDirectory(pasta); } catch { RespJson(s, 400, new Dictionary<string, object> { { "erro", "Pasta invalida." } }, false); return; } }
                lock (Config.D) { Config.D.apelido = apelido; Config.D.navegador = nav; Config.D.pastaRecebidos = pasta; Config.Salvar(); }
                RespJson(s, 200, new Dictionary<string, object> { { "ok", true } }, false); return;
            }
            if (c == "/api/painel/abrir-recebidos")
            {
                try { Arquivos.Abrir(Arquivos.PastaRecebidos()); } catch { }
                RespJson(s, 200, new Dictionary<string, object> { { "ok", true } }, false); return;
            }
            if (c == "/api/painel/codigo") { NovoCodigo(); RespJson(s, 200, new Dictionary<string, object> { { "codigo", Codigo } }, false); return; }
            if (c == "/api/painel/revogar")
            {
                string id = Texto(LerJson(r.Corpo), "id");
                lock (Config.D) { Config.D.dispositivos.RemoveAll(x => x.id == id); Config.Salvar(); }
                RespJson(s, 200, new Dictionary<string, object> { { "ok", true } }, false); return;
            }
            if (c == "/api/painel/sites")
            {
                int total = SalvarSites(r.Corpo);
                if (total < 0) Responder(s, 400, "text/plain", new byte[0], false);
                else RespJson(s, 200, new Dictionary<string, object> { { "ok", true }, { "total", total } }, false);
                return;
            }
            Responder(s, 404, "text/plain", new byte[0], false);
        }

        // Valida e grava a lista de botoes de sites ({"sites":[{titulo,url,cor}]}). Devolve o total, ou -1 se invalido.
        static int SalvarSites(string corpo)
        {
            try
            {
                var lista = Json.Deserialize<Dictionary<string, List<Site>>>(corpo)["sites"];
                var novos = new List<Site>();
                var usados = new HashSet<string>();
                foreach (Site x in lista)
                {
                    if (string.IsNullOrWhiteSpace(x.titulo) || !Navegador.UrlValida(x.url)) continue;
                    string baseId = new string(x.titulo.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
                    if (baseId == "") baseId = "site";
                    string id = baseId; int n = 2;
                    while (!usados.Add(id)) id = baseId + (n++);
                    string cor = x.cor != null && System.Text.RegularExpressions.Regex.IsMatch(x.cor, "^#[0-9a-fA-F]{6}$") ? x.cor : "#3b4252";
                    novos.Add(new Site { id = id, titulo = x.titulo.Trim(), url = x.url, cor = cor });
                    if (novos.Count >= 40) break;
                }
                lock (Config.D) { Config.D.sites = novos; Config.Salvar(); }
                return novos.Count;
            }
            catch { return -1; }
        }

        static string MacLan()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    bool gw = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    if (gw) return BitConverter.ToString(ni.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":");
                }
            }
            catch { }
            return "";
        }

        public static string NomeNavegador()
        {
            string exe = Navegador.Achar();
            return exe == null ? "" : Path.GetFileNameWithoutExtension(exe);
        }

        public static List<string> IpsLan()
        {
            var comGateway = new List<string>();
            var outros = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    bool gw = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        (gw ? comGateway : outros).Add(ip);
                    }
                }
            }
            catch { }
            return comGateway.Count > 0 ? comGateway : outros;
        }

        // ---------- WebSocket (so o necessario)

        void WebSocket(NetworkStream s, Requisicao r)
        {
            string chave = r.Cabecalho("sec-websocket-key");
            if (chave == "" || r.Cabecalho("upgrade").ToLowerInvariant() != "websocket") { Responder(s, 400, "text/plain", new byte[0], false); return; }
            string aceita;
            using (var sha = SHA1.Create())
                aceita = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(chave + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            byte[] resp = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + aceita + "\r\n\r\n");
            s.Write(resp, 0, resp.Length);
            s.ReadTimeout = 60000;
            var canal = new Canal { S = s };
            string idDisp = IdDispositivo(r);
            Marcar(idDisp, +1);
            try
            {
                while (true)
                {
                    int op;
                    string msg = LerFrame(s, out op);
                    if (op == 8) return;
                    if (op == 9) { canal.Pong(); continue; }
                    if (op == 1 && msg != null) { Comando(msg, canal); if (idDisp != null) lock (Online) { UltimoUso[idDisp] = DateTime.Now; } }
                }
            }
            finally { Marcar(idDisp, -1); }
        }

        static void LerTudo(Stream s, byte[] dest, int n)
        {
            int lido = 0;
            while (lido < n) { int k = s.Read(dest, lido, n - lido); if (k <= 0) throw new IOException("fechado"); lido += k; }
        }

        static string LerFrame(Stream s, out int op)
        {
            byte[] h = new byte[2];
            LerTudo(s, h, 2);
            op = h[0] & 0x0F;
            bool mascara = (h[1] & 0x80) != 0;
            long tam = h[1] & 0x7F;
            if (tam == 126) { byte[] e = new byte[2]; LerTudo(s, e, 2); tam = (e[0] << 8) | e[1]; }
            else if (tam == 127) { byte[] e = new byte[8]; LerTudo(s, e, 8); tam = 0; foreach (byte x in e) tam = (tam << 8) | x; }
            if (tam > (1 << 20)) { op = 8; return null; }
            byte[] mk = new byte[4];
            if (mascara) LerTudo(s, mk, 4);
            byte[] dados = new byte[tam];
            if (tam > 0) LerTudo(s, dados, (int)tam);
            if (mascara) for (int i = 0; i < dados.Length; i++) dados[i] ^= mk[i % 4];
            return op == 1 ? Encoding.UTF8.GetString(dados) : null;
        }

        public static void EnviarTexto(Stream s, string texto) { EnviarFrame(s, 1, texto); }

        public static void EnviarFrame(Stream s, int op, string texto)
        {
            byte[] d = Encoding.UTF8.GetBytes(texto);
            var ms = new MemoryStream();
            ms.WriteByte((byte)(0x80 | op));
            if (d.Length < 126) ms.WriteByte((byte)d.Length);
            else if (d.Length < 65536) { ms.WriteByte(126); ms.WriteByte((byte)(d.Length >> 8)); ms.WriteByte((byte)d.Length); }
            else { ms.WriteByte(127); for (int sh = 56; sh >= 0; sh -= 8) ms.WriteByte((byte)((long)d.Length >> sh)); }
            ms.Write(d, 0, d.Length);
            byte[] b = ms.ToArray();
            s.Write(b, 0, b.Length);
        }

        // Comandos vindos do celular:
        //  m dx dy | b l|r|m d|u|c | w delta | k combo | t texto | o idDoSite | u url | n
        //  a nx ny (mouse absoluto 0..10000) | z delta (zoom: ctrl + roda)
        //  vg | vs 0..1 | jl | jf h | jm h | jc h | pw acao | pt minutos | pq | cg | cs base64 | ag
        // As respostas (so pelo WebSocket) voltam como "tipo json". Pelo /api/cmd o canal e nulo.
        //  al | as id (audio) | sx idCena | bg | bs 0..100 (brilho) | ml (monitores) | am idx nx ny | fo base64Caminho
        internal static void Comando(string msg, Canal canal)
        {
            try
            {
                string[] p = msg.Split(new char[] { ' ' }, 2);
                string arg = p.Length > 1 ? p[1] : "";
                switch (p[0])
                {
                    case "al":
                        if (canal != null) canal.Enviar("aud", Json.Serialize(Audio.Listar()));
                        break;
                    case "as":
                        if (arg.Length > 0 && arg.Length < 300) Audio.Definir(arg);
                        if (canal != null) canal.Enviar("aud", Json.Serialize(Audio.Listar()));
                        break;
                    case "sx": Cenas.Executar(arg); break;
                    case "bg":
                        if (canal != null) canal.Enviar("bri", Json.Serialize(new Dictionary<string, object> { { "v", Brilho.Ler() } }));
                        break;
                    case "bs": Brilho.Definir(int.Parse(arg)); break;
                    case "ml":
                        if (canal != null) canal.Enviar("mon", Json.Serialize(Sistema.Monitores()));
                        break;
                    case "am":
                        {
                            string[] a = arg.Split(' ');
                            Sistema.MoverAbs(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[0])); break;
                        }
                    case "fo": Arquivos.Abrir(Encoding.UTF8.GetString(Convert.FromBase64String(arg))); break;
                    case "a":
                        {
                            string[] a = arg.Split(' ');
                            Sistema.MoverAbs(int.Parse(a[0]), int.Parse(a[1])); break;
                        }
                    case "z":
                        {
                            int d = int.Parse(arg);
                            Entrada.CtrlRoda(d); break;
                        }
                    case "vg":
                        if (canal != null)
                            canal.Enviar("vol", Json.Serialize(new Dictionary<string, object> { { "v", Math.Round(Volume.Ler(), 3) }, { "m", Volume.Mudo() } }));
                        break;
                    case "vs":
                        Volume.Definir(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)); break;
                    case "jl":
                        if (canal != null) canal.Enviar("jan", Json.Serialize(Sistema.Janelas()));
                        break;
                    case "jf": Sistema.Focar(long.Parse(arg)); break;
                    case "jm": Sistema.Minimizar(long.Parse(arg)); break;
                    case "jc": Sistema.Fechar(long.Parse(arg)); break;
                    case "pw": Sistema.Energia(arg); break;
                    case "pt": Sistema.Temporizador(int.Parse(arg)); break;
                    case "pq":
                        if (canal != null) canal.Enviar("tmr", Json.Serialize(new Dictionary<string, object> { { "s", Sistema.Restante() } }));
                        break;
                    case "cg":
                        if (canal != null) canal.Enviar("clip", Json.Serialize(new Dictionary<string, object> { { "t", Sistema.LerClip() } }));
                        break;
                    case "cs":
                        Sistema.EscreverClip(Encoding.UTF8.GetString(Convert.FromBase64String(arg))); break;
                    case "ag":
                        if (canal != null)
                            new Thread(delegate () { canal.Enviar("now", Midia.Agora()); }) { IsBackground = true }.Start();
                        break;
                    case "m":
                        {
                            string[] a = arg.Split(' ');
                            Entrada.Mover(int.Parse(a[0]), int.Parse(a[1])); break;
                        }
                    case "b":
                        {
                            string[] a = arg.Split(' ');
                            Entrada.Botao(a[0][0], a[1][0]); break;
                        }
                    case "w": Entrada.Roda(int.Parse(arg)); break;
                    case "k": Entrada.Combinacao(arg); break;
                    case "t": Entrada.Texto(arg); break;
                    case "o":
                        {
                            Site s;
                            lock (Config.D) { s = Config.D.sites.FirstOrDefault(x => x.id == arg); }
                            if (s != null) Navegador.Abrir(s.url);
                            break;
                        }
                    case "u": Navegador.Abrir(arg); break;
                    case "n": Navegador.AbrirNavegador(); break;
                }
            }
            catch (Exception e) { Config.Log("comando '" + msg + "': " + e.Message); }
        }
    }

    // ------------------------------------------------------------------ bandeja

    class Bandeja : ApplicationContext
    {
        readonly NotifyIcon icone;
        readonly Servidor servidor;
        const string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public Bandeja(Servidor srv)
        {
            servidor = srv;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir painel (código e QR Code)", null, delegate { AbrirPainel(); });
            menu.Items.Add("Abrir pasta dos arquivos recebidos", null, delegate { try { Arquivos.Abrir(Arquivos.PastaRecebidos()); } catch { } });
            var auto = new ToolStripMenuItem("Iniciar com o Windows") { Checked = AutoIniciar() };
            auto.Click += delegate { DefinirAutoIniciar(!auto.Checked); auto.Checked = AutoIniciar(); };
            menu.Items.Add(auto);
            menu.Items.Add("Sair", null, delegate { icone.Visible = false; servidor.Parar(); Application.Exit(); });
            Icon marca = SystemIcons.Application;
            try { Icon ex = Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (ex != null) marca = ex; } catch { }
            icone = new NotifyIcon
            {
                Icon = marca,
                Text = "Controle Universal",
                ContextMenuStrip = menu,
                Visible = true
            };
            icone.DoubleClick += delegate { AbrirPainel(); };
        }

        public void Aviso(string t)
        {
            icone.BalloonTipTitle = "Controle Universal";
            icone.BalloonTipText = t;
            icone.ShowBalloonTip(5000);
        }

        public static void AbrirPainel()
        {
            try { Process.Start(new ProcessStartInfo("http://localhost:" + Config.D.porta + "/painel") { UseShellExecute = true }); }
            catch { }
        }

        static bool AutoIniciar()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ChaveRun))
                return k != null && k.GetValue("ControleUniversal") != null;
        }

        static void DefinirAutoIniciar(bool ligar)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ChaveRun, true))
            {
                if (ligar) k.SetValue("ControleUniversal", "\"" + Application.ExecutablePath + "\" --silencioso");
                else k.DeleteValue("ControleUniversal", false);
            }
        }
    }

    static class Programa
    {
        [STAThread]
        static void Main(string[] args)
        {
            Sistema.SetProcessDPIAware();     // coordenadas em pixels reais (captura de tela e mouse absoluto)
            Application.EnableVisualStyles();
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;   // HTTPS (TLS 1.2) para buscar icones
            Config.Carregar();

            // O proprio .exe e o instalador: --instalar, --desinstalar, ou pergunta ao abrir de fora da pasta de instalacao.
            if (args.Any(a => a == "--desinstalar")) { Instalador.Desinstalar(); return; }
            if (args.Any(a => a == "--instalar")) { Instalador.Instalar(); return; }
            if (!Instalador.RodandoDaInstalacao() && !args.Any(a => a == "--portatil"))
            {
                string msg = Instalador.EstaInstalado()
                    ? "Já existe uma versão do Controle Universal instalada. Atualizar para esta versão?\n\n(Sim = atualizar, Não = só abrir esta cópia sem instalar)"
                    : "Instalar o Controle Universal neste computador?\n\nEle fica na bandeja do sistema e inicia junto com o Windows.\n(Sim = instalar, Não = só abrir esta cópia sem instalar)";
                DialogResult resp = MessageBox.Show(msg, "Controle Universal", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (resp == DialogResult.Cancel) return;
                if (resp == DialogResult.Yes) { Instalador.Instalar(); return; }
            }

            bool primeira;
            Mutex unica = new Mutex(true, "Local\\ControleUniversal-unico", out primeira);
            if (!primeira) { Bandeja.AbrirPainel(); return; }

            var servidor = new Servidor();
            try { servidor.Iniciar(); }
            catch (Exception e)
            {
                MessageBox.Show("Não consegui abrir a porta " + Config.D.porta + ". Outro programa pode estar usando.\n\n" + e.Message,
                    "Controle Universal", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var bandeja = new Bandeja(servidor);
            bool silencioso = args.Any(a => a == "--silencioso");
            if (!silencioso) Bandeja.AbrirPainel();
            else bandeja.Aviso("Pronto. Abra o painel pelo ícone para ver o código de pareamento.");
            Application.Run(bandeja);
            GC.KeepAlive(unica);
        }
    }
}

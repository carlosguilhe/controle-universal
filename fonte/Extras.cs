// Recursos extras do Controle Universal: audio, cenas, agenda, brilho, arquivos e instalador.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Controle Universal")]
[assembly: AssemblyProduct("Controle Universal")]
[assembly: AssemblyDescription("Controle o PC pelo celular: mouse, teclado, midia, sites, tela e arquivos.")]
[assembly: AssemblyCompany("Controle Universal")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("4.1.0.0")]
[assembly: AssemblyFileVersion("4.1.0.0")]

namespace ControleUniversal
{
    // ------------------------------------------------------------------ dados

    public class Cena
    {
        public string id;
        public string nome;
        public List<string> passos = new List<string>();
    }

    public class Agenda
    {
        public string id;
        public string titulo;
        public string hora;        // "HH:mm"
        public bool diario;
        public string proximo;     // data/hora local ISO da proxima execucao
        public List<string> passos = new List<string>();
    }

    // ------------------------------------------- dispositivos de audio (CoreAudio)

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { int GetCount(out uint n); int Item(uint i, out IMMDevice d); }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort vt; public ushort r1; public ushort r2; public ushort r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        int GetCount(out uint n);
        int GetAt(uint i, out PROPERTYKEY k);
        int GetValue(ref PROPERTYKEY k, out PropVariant v);
        int SetValue(ref PROPERTYKEY k, ref PropVariant v);
        int Commit();
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        int f0(); int f1(); int f2(); int f3(); int f4(); int f5(); int f6(); int f7(); int f8(); int f9();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int funcao);
        int f11();
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigClient { }

    static class Audio
    {
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant p);

        static string NomeDe(IMMDevice d)
        {
            try
            {
                IPropertyStore ps; d.OpenPropertyStore(0, out ps);
                PROPERTYKEY k = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
                PropVariant v; ps.GetValue(ref k, out v);
                string s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : "";
                PropVariantClear(ref v);
                return s ?? "";
            }
            catch { return ""; }
        }

        public static List<Dictionary<string, object>> Listar()
        {
            var lista = new List<Dictionary<string, object>>();
            var e = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            string padrao = "";
            try { IMMDevice dp; e.GetDefaultAudioEndpoint(0, 1, out dp); dp.GetId(out padrao); } catch { }
            IMMDeviceCollection col; e.EnumAudioEndpoints(0, 1, out col);
            uint n; col.GetCount(out n);
            for (uint i = 0; i < n; i++)
            {
                IMMDevice d; col.Item(i, out d);
                string id; d.GetId(out id);
                lista.Add(new Dictionary<string, object> { { "id", id }, { "n", NomeDe(d) }, { "p", id == padrao } });
            }
            return lista;
        }

        public static void Definir(string id)
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            for (int funcao = 0; funcao < 3; funcao++) pc.SetDefaultEndpoint(id, funcao);
        }
    }

    // ------------------------------------------------------------------- cenas

    static class Cenas
    {
        // Comandos que uma cena pode usar (os mesmos do celular, sem os que so servem para resposta).
        static readonly string[] Permitidos = { "k", "t", "o", "u", "n", "vs", "pw", "pt", "d", "as", "b", "w", "m", "a", "z" };

        static List<string> Limpar(IEnumerable<string> passos)
        {
            var r = new List<string>();
            foreach (string p0 in passos ?? new List<string>())
            {
                string p = (p0 ?? "").Trim();
                if (p.Length == 0 || p.Length > 400) continue;
                string cab = p.Split(' ')[0];
                if (Array.IndexOf(Permitidos, cab) < 0) continue;
                r.Add(p);
                if (r.Count >= 30) break;
            }
            return r;
        }

        static string Id(string nome, HashSet<string> usados)
        {
            string b = new string((nome ?? "").ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
            if (b == "") b = "cena";
            string id = b; int n = 2;
            while (!usados.Add(id)) id = b + (n++);
            return id;
        }

        // {"cenas":[{nome, passos:[...]}]} -> total, ou -1
        public static int Salvar(string corpo)
        {
            try
            {
                var lista = new JavaScriptSerializer().Deserialize<Dictionary<string, List<Cena>>>(corpo)["cenas"];
                var novas = new List<Cena>(); var usados = new HashSet<string>();
                foreach (Cena c in lista)
                {
                    if (string.IsNullOrWhiteSpace(c.nome)) continue;
                    novas.Add(new Cena { id = Id(c.nome, usados), nome = c.nome.Trim(), passos = Limpar(c.passos) });
                    if (novas.Count >= 30) break;
                }
                lock (Config.D) { Config.D.cenas = novas; Config.Salvar(); }
                return novas.Count;
            }
            catch { return -1; }
        }

        public static void Executar(string id)
        {
            Cena c;
            lock (Config.D) { c = Config.D.cenas.FirstOrDefault(x => x.id == id); }
            if (c != null) Passos(c.passos);
        }

        public static void Passos(List<string> passos)
        {
            var copia = new List<string>(passos ?? new List<string>());
            new Thread(delegate ()
            {
                foreach (string p in copia)
                {
                    try
                    {
                        if (p.StartsWith("d "))
                        {
                            int ms; if (int.TryParse(p.Substring(2), out ms)) Thread.Sleep(Math.Max(0, Math.Min(ms, 60000)));
                        }
                        else Servidor.Comando(p, null);
                    }
                    catch (Exception e) { Config.Log("cena: " + e.Message); }
                }
            }) { IsBackground = true }.Start();
        }
    }

    // ------------------------------------------------------------------ agenda

    static class Agendador
    {
        public static void Iniciar() { new Thread(Laco) { IsBackground = true }.Start(); }

        static void Laco()
        {
            while (true)
            {
                Thread.Sleep(15000);
                try { Verificar(); } catch (Exception e) { Config.Log("agenda: " + e.Message); }
            }
        }

        static DateTime? Parse(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? (DateTime?)d : null;
        }

        static void Verificar()
        {
            DateTime agora = DateTime.Now;
            var rodar = new List<Agenda>(); bool mudou = false;
            lock (Config.D)
            {
                foreach (Agenda a in Config.D.agenda.ToList())
                {
                    DateTime? p = Parse(a.proximo);
                    if (p == null || p.Value > agora) continue;
                    if ((agora - p.Value).TotalMinutes <= 5) rodar.Add(a);       // PC ligado a tempo
                    mudou = true;
                    if (a.diario) { DateTime q = p.Value; while (q <= agora) q = q.AddDays(1); a.proximo = q.ToString("s", CultureInfo.InvariantCulture); }
                    else Config.D.agenda.Remove(a);
                }
                if (mudou) Config.Salvar();
            }
            foreach (Agenda a in rodar) Cenas.Passos(a.passos);
        }

        public static string ProximaData(string hora)
        {
            string[] p = (hora ?? "").Split(':');
            int h = int.Parse(p[0]), m = int.Parse(p[1]);
            DateTime d = DateTime.Today.AddHours(h).AddMinutes(m);
            if (d <= DateTime.Now) d = d.AddDays(1);
            return d.ToString("s", CultureInfo.InvariantCulture);
        }

        // {"itens":[{titulo, hora, diario, passos}]} -> total, ou -1
        public static int Salvar(string corpo)
        {
            try
            {
                var lista = new JavaScriptSerializer().Deserialize<Dictionary<string, List<Agenda>>>(corpo)["itens"];
                var novos = new List<Agenda>();
                foreach (Agenda a in lista)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(a.hora ?? "", "^([01]?[0-9]|2[0-3]):[0-5][0-9]$")) continue;
                    var passos = new List<string>();
                    foreach (string p in a.passos ?? new List<string>())
                    {
                        string t = (p ?? "").Trim(); if (t.Length == 0 || t.Length > 400) continue;
                        string cab = t.Split(' ')[0];
                        if (Array.IndexOf(new string[] { "k", "t", "o", "u", "n", "vs", "pw", "pt", "d", "as", "sx" }, cab) < 0) continue;
                        passos.Add(t);
                    }
                    if (passos.Count == 0) continue;
                    string hora = a.hora.Length == 4 ? "0" + a.hora : a.hora;
                    bool manter = !string.IsNullOrEmpty(a.proximo) && Parse(a.proximo) != null && Parse(a.proximo).Value > DateTime.Now && Parse(a.proximo).Value.ToString("HH:mm") == hora;
                    novos.Add(new Agenda {
                        id = string.IsNullOrEmpty(a.id) ? Guid.NewGuid().ToString("N").Substring(0, 8) : a.id,
                        titulo = string.IsNullOrWhiteSpace(a.titulo) ? "Tarefa" : a.titulo.Trim(),
                        hora = hora, diario = a.diario, passos = passos,
                        proximo = manter ? a.proximo : ProximaData(hora) });
                    if (novos.Count >= 30) break;
                }
                lock (Config.D) { Config.D.agenda = novos; Config.Salvar(); }
                return novos.Count;
            }
            catch { return -1; }
        }
    }

    // ------------------------------------------------------------------ brilho

    static class Brilho
    {
        // So existe em telas de notebook; em monitor comum devolve -1.
        public static int Ler()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness"))
                    foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }
        public static void Definir(int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v });
            }
            catch { }
        }
    }

    // ------------------------------------------------- icones dos sites (cache no PC)

    static class Icones
    {
        static readonly Dictionary<string, DateTime> Falhas = new Dictionary<string, DateTime>();
        static readonly object Trava = new object();

        static string Pasta
        {
            get { string p = Path.Combine(Config.Pasta, "icones"); Directory.CreateDirectory(p); return p; }
        }

        // So aceita nomes de site publicos (sem IP, sem porta, sem "localhost").
        public static string HostDe(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || url.Length > 300 || !Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            string h = u.Host.ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(h, @"^[a-z0-9]([a-z0-9.-]*[a-z0-9])?\.[a-z]{2,}$")) return null;
            return h;
        }

        static bool Privado(string host)
        {
            try
            {
                foreach (System.Net.IPAddress ip in System.Net.Dns.GetHostAddresses(host))
                {
                    if (System.Net.IPAddress.IsLoopback(ip)) return true;
                    byte[] b = ip.GetAddressBytes();
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        if (b[0] == 10 || b[0] == 0 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)) return true;
                    }
                    else if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
                }
                return false;
            }
            catch { return true; }
        }

        public static string Tipo(byte[] b)
        {
            if (b == null || b.Length < 16) return null;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return "image/x-icon";
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
            if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
            if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return "image/webp";
            return null;
        }

        static byte[] Baixar(string url)
        {
            try
            {
                var rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                rq.Timeout = 5000; rq.ReadWriteTimeout = 5000; rq.MaximumAutomaticRedirections = 3;
                rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ControleUniversal";
                using (var rs = (System.Net.HttpWebResponse)rq.GetResponse())
                {
                    if ((int)rs.StatusCode != 200 || rs.ContentLength > 524288) return null;
                    using (Stream st = rs.GetResponseStream())
                    {
                        var ms = new MemoryStream(); byte[] buf = new byte[8192]; int k;
                        while ((k = st.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, k); if (ms.Length > 524288) return null; }
                        return ms.ToArray();
                    }
                }
            }
            catch { return null; }
        }

        // Devolve o icone do site (do cache ou da internet) ou null se nao existir.
        public static byte[] Obter(string host, out string tipo)
        {
            tipo = null;
            string arq = Path.Combine(Pasta, host + ".img");
            if (File.Exists(arq))
            {
                byte[] c = File.ReadAllBytes(arq); tipo = Tipo(c);
                if (tipo != null) return c;
            }
            lock (Trava)
            {
                DateTime quando;
                if (Falhas.TryGetValue(host, out quando) && (DateTime.Now - quando).TotalMinutes < 30) return null;
            }
            // 1) servico de favicons do Google (PNG nitido, 404 quando o site nao tem icone), 2) icone de toque do proprio site, 3) favicon.ico
            string[] tentativas = {
                "https://www.google.com/s2/favicons?domain=" + host + "&sz=128",
                "https://" + host + "/apple-touch-icon.png",
                "https://" + host + "/favicon.ico" };
            for (int i = 0; i < tentativas.Length; i++)
            {
                if (i > 0 && Privado(host)) break;
                byte[] b = Baixar(tentativas[i]); string t = Tipo(b);
                if (t != null) { try { File.WriteAllBytes(arq, b); } catch { } tipo = t; return b; }
            }
            lock (Trava) { Falhas[host] = DateTime.Now; }
            return null;
        }
    }

    // ---------------------------------------------------------------- arquivos

    static class Arquivos
    {
        static string Perfil { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

        public static string PastaRecebidos()
        {
            string p = Config.D.pastaRecebidos;
            if (string.IsNullOrWhiteSpace(p)) p = Path.Combine(Perfil, "Downloads", "Controle Universal");
            Directory.CreateDirectory(p);
            return p;
        }

        static Dictionary<string, object> Pasta(string nome, string caminho)
        {
            return new Dictionary<string, object> { { "n", nome }, { "t", "d" }, { "s", 0L }, { "c", caminho } };
        }

        public static Dictionary<string, object> Raiz()
        {
            var itens = new List<Dictionary<string, object>>();
            Action<string, string> add = delegate (string nome, string c) { if (!string.IsNullOrEmpty(c) && Directory.Exists(c)) itens.Add(Pasta(nome, c)); };
            add("Área de Trabalho", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            add("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            add("Downloads", Path.Combine(Perfil, "Downloads"));
            add("Vídeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            add("Imagens", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            add("Música", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            add("Recebidos do celular", PastaRecebidos());
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                    if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                        itens.Add(Pasta("Disco " + d.Name.TrimEnd('\\'), d.Name));
            }
            catch { }
            return new Dictionary<string, object> { { "c", "@" }, { "pai", "" }, { "itens", itens } };
        }

        public static Dictionary<string, object> Listar(string caminho)
        {
            if (string.IsNullOrEmpty(caminho) || caminho == "@") return Raiz();
            string c = Path.GetFullPath(caminho);
            if (!Directory.Exists(c)) throw new DirectoryNotFoundException();
            var itens = new List<Dictionary<string, object>>();
            var di = new DirectoryInfo(c);
            try
            {
                foreach (DirectoryInfo d in di.GetDirectories().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    itens.Add(Pasta(d.Name, d.FullName));
                    if (itens.Count >= 400) break;
                }
                foreach (FileInfo f in di.GetFiles().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    itens.Add(new Dictionary<string, object> { { "n", f.Name }, { "t", "f" }, { "s", f.Length }, { "c", f.FullName } });
                    if (itens.Count >= 800) break;
                }
            }
            catch (UnauthorizedAccessException) { }
            string pai = di.Parent != null ? di.Parent.FullName : "@";
            return new Dictionary<string, object> { { "c", c }, { "pai", pai }, { "itens", itens } };
        }

        public static void Abrir(string caminho)
        {
            string c = Path.GetFullPath(caminho);
            if (!File.Exists(c) && !Directory.Exists(c)) return;
            Process.Start(new ProcessStartInfo(c) { UseShellExecute = true });
        }

        public static string NomeSeguro(string nome)
        {
            nome = Path.GetFileName(nome ?? "");
            foreach (char ch in Path.GetInvalidFileNameChars()) nome = nome.Replace(ch, '_');
            nome = nome.Trim().TrimEnd('.');
            if (nome.Length == 0) nome = "arquivo";
            if (nome.Length > 150) nome = nome.Substring(nome.Length - 150);
            return nome;
        }

        public static string NomeUnico(string pasta, string nome)
        {
            string baseNome = Path.GetFileNameWithoutExtension(nome), ext = Path.GetExtension(nome);
            string alvo = Path.Combine(pasta, nome); int n = 1;
            while (File.Exists(alvo)) alvo = Path.Combine(pasta, baseNome + " (" + (n++) + ")" + ext);
            return alvo;
        }
    }

    // ---------------------------------------------------------------- instalador

    static class Instalador
    {
        const string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ChaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ControleUniversal";

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ControleUniversal"); } }
        public static string ExeInstalado { get { return Path.Combine(Dir, "ControleUniversal.exe"); } }
        public static bool EstaInstalado() { return File.Exists(ExeInstalado); }
        public static bool RodandoDaInstalacao()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(ExeInstalado), StringComparison.OrdinalIgnoreCase);
        }
        static string Atalho { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Controle Universal.lnk"); } }

        static void Encerrar()
        {
            int meu = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("ControleUniversal"))
            {
                if (p.Id == meu) continue;
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        // Roda comandos de firewall como administrador (pede permissao uma vez). Devolve false se negado.
        static bool Admin(string linhaDeComando)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + linhaDeComando) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                using (Process p = Process.Start(psi)) { p.WaitForExit(30000); return p.ExitCode == 0; }
            }
            catch { return false; }
        }

        static void CriarAtalho(string lnk, string alvo)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object s = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                Type ts = s.GetType();
                ts.InvokeMember("TargetPath", BindingFlags.SetProperty, null, s, new object[] { alvo });
                ts.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, s, new object[] { Path.GetDirectoryName(alvo) });
                ts.InvokeMember("Description", BindingFlags.SetProperty, null, s, new object[] { "Controle Universal" });
                ts.InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
            }
            catch (Exception e) { Config.Log("atalho: " + e.Message); }
        }

        public static void Instalar()
        {
            Encerrar();
            Directory.CreateDirectory(Dir);
            string origem = Path.GetFullPath(Application.ExecutablePath);
            if (!string.Equals(origem, Path.GetFullPath(ExeInstalado), StringComparison.OrdinalIgnoreCase))
                File.Copy(origem, ExeInstalado, true);

            bool fw = Admin("netsh advfirewall firewall delete rule name=\"Controle Universal\" & netsh advfirewall firewall add rule name=\"Controle Universal\" dir=in action=allow protocol=TCP localport="
                + Config.D.porta + " program=\"" + ExeInstalado + "\" profile=any");

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ChaveRun))
                k.SetValue("ControleUniversal", "\"" + ExeInstalado + "\" --silencioso");
            CriarAtalho(Atalho, ExeInstalado);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ChaveDesinstalar))
            {
                k.SetValue("DisplayName", "Controle Universal");
                k.SetValue("DisplayVersion", "4.1.0");
                k.SetValue("Publisher", "Controle Universal");
                k.SetValue("DisplayIcon", ExeInstalado);
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + ExeInstalado + "\" --desinstalar");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            }
            if (!fw)
                MessageBox.Show("O programa foi instalado, mas a permissão do firewall não foi concedida. O celular pode não conseguir conectar.\n\nRode a instalação de novo e aceite o pedido de administrador.",
                    "Controle Universal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Process.Start(new ProcessStartInfo(ExeInstalado) { WorkingDirectory = Dir });
        }

        public static void Desinstalar()
        {
            if (MessageBox.Show("Remover o Controle Universal deste computador?", "Controle Universal", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool apagarDados = MessageBox.Show("Apagar também os celulares pareados e as configurações?", "Controle Universal", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            Encerrar();
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ChaveRun, true)) { if (k != null) k.DeleteValue("ControleUniversal", false); } } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(ChaveDesinstalar, false); } catch { }
            try { if (File.Exists(Atalho)) File.Delete(Atalho); } catch { }
            Admin("netsh advfirewall firewall delete rule name=\"Controle Universal\"");
            if (apagarDados) { try { Directory.Delete(Config.Pasta, true); } catch { } }
            // o programa nao consegue apagar a propria pasta enquanto roda: um cmd faz isso logo depois
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
            MessageBox.Show("Controle Universal removido.", "Controle Universal", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

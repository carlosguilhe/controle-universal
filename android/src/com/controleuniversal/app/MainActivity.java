package com.controleuniversal.app;

import android.Manifest;
import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.pm.ShortcutInfo;
import android.content.pm.ShortcutManager;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.drawable.Icon;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebSettings;
import android.webkit.WebView;

import org.json.JSONObject;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.util.Collections;

public class MainActivity extends Activity {
    static final String CANAL = "controle";
    static final int ID_NOTIFICACAO = 1;
    private WebView web;
    /** O que veio de fora (link compartilhado ou atalho da tela inicial), em JSON. A tela busca com pegarPendente(). */
    private volatile String pendente = "";
    /** Verdadeiro enquanto a tela de controle esta aberta (a tela HTML avisa). */
    private volatile boolean emControle = false;

    @Override
    protected void onCreate(Bundle estado) {
        super.onCreate(estado);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        getWindow().setStatusBarColor(Color.parseColor("#111114"));
        getWindow().setNavigationBarColor(Color.parseColor("#111114"));

        web = new WebView(this);
        web.setBackgroundColor(Color.parseColor("#111114"));
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setAllowFileAccess(true);
        s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        s.setTextZoom(100);
        s.setSupportZoom(false);
        web.setOverScrollMode(View.OVER_SCROLL_NEVER);
        web.addJavascriptInterface(new Ponte(), "Android");
        setContentView(web);
        lerIntent(getIntent());
        web.loadUrl("file:///android_asset/index.html");
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        lerIntent(intent);
        web.post(() -> web.evaluateJavascript("window.cuIntent && window.cuIntent()", null));
    }

    /** Arquivos compartilhados que esperam a conexao com o PC para serem enviados. */
    private final java.util.List<Uri> arquivosPendentes = new java.util.ArrayList<>();

    @SuppressWarnings("deprecation")
    private void juntarArquivos(Intent i) {
        arquivosPendentes.clear();
        try {
            if (Intent.ACTION_SEND.equals(i.getAction())) {
                Uri u = i.getParcelableExtra(Intent.EXTRA_STREAM);
                if (u != null) arquivosPendentes.add(u);
            } else if (Intent.ACTION_SEND_MULTIPLE.equals(i.getAction())) {
                java.util.ArrayList<Uri> l = i.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
                if (l != null) arquivosPendentes.addAll(l);
            }
        } catch (Exception ignorar) { }
    }

    /** Link compartilhado ("Compartilhar" > Controle Universal) ou atalho de site na tela inicial. */
    private void lerIntent(Intent i) {
        if (i == null) return;
        try {
            JSONObject j = new JSONObject();
            juntarArquivos(i);
            if (!arquivosPendentes.isEmpty()) {
                j.put("tipo", "arquivos").put("n", arquivosPendentes.size());
                pendente = j.toString();
            } else if (Intent.ACTION_SEND.equals(i.getAction()) && i.getStringExtra(Intent.EXTRA_TEXT) != null) {
                j.put("tipo", "link").put("valor", i.getStringExtra(Intent.EXTRA_TEXT));
                pendente = j.toString();
            } else if (i.getStringExtra("site") != null) {
                j.put("tipo", "site").put("pc", i.getStringExtra("pc")).put("site", i.getStringExtra("site"));
                pendente = j.toString();
            }
        } catch (Exception ignorar) { }
    }

    // ------------------------------------------------------------ voz (ditado)

    private static final int PEDIDO_VOZ = 42;

    @Override
    protected void onActivityResult(int pedido, int resultado, Intent dados) {
        super.onActivityResult(pedido, resultado, dados);
        if (pedido != PEDIDO_VOZ) return;
        String texto = "";
        if (resultado == RESULT_OK && dados != null) {
            java.util.ArrayList<String> r = dados.getStringArrayListExtra(android.speech.RecognizerIntent.EXTRA_RESULTS);
            if (r != null && !r.isEmpty()) texto = r.get(0);
        }
        final String js = "window.cuVoz && window.cuVoz(" + JSONObject.quote(texto) + ")";
        web.post(() -> web.evaluateJavascript(js, null));
    }

    // ------------------------------------------------------------ envio de arquivos para o PC

    private void avisarEnvio(final JSONObject o) {
        web.post(() -> web.evaluateJavascript("window.cuUpload && window.cuUpload(" + o.toString() + ")", null));
    }

    private void enviarArquivos(final String ip, final int porta, final String token, final boolean abrir) {
        final java.util.List<Uri> lista = new java.util.ArrayList<>(arquivosPendentes);
        arquivosPendentes.clear();
        new Thread(() -> {
            int ok = 0;
            for (int idx = 0; idx < lista.size(); idx++) {
                Uri uri = lista.get(idx);
                String nome = "arquivo";
                long tam = -1;
                try (android.database.Cursor c = getContentResolver().query(uri, null, null, null, null)) {
                    if (c != null && c.moveToFirst()) {
                        int n = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME);
                        int s = c.getColumnIndex(android.provider.OpenableColumns.SIZE);
                        if (n >= 0 && c.getString(n) != null) nome = c.getString(n);
                        if (s >= 0 && !c.isNull(s)) tam = c.getLong(s);
                    }
                } catch (Exception ignorar) { }
                java.io.File temp = null;
                try {
                    java.io.InputStream in = getContentResolver().openInputStream(uri);
                    if (in == null) throw new java.io.IOException("sem acesso");
                    if (tam <= 0) {            // tamanho desconhecido: copia para o cache para saber quanto vai enviar
                        temp = java.io.File.createTempFile("env", ".tmp", getCacheDir());
                        try (java.io.OutputStream out = new java.io.FileOutputStream(temp)) {
                            byte[] b = new byte[65536]; int k;
                            while ((k = in.read(b)) > 0) out.write(b, 0, k);
                        }
                        in.close();
                        tam = temp.length();
                        in = new java.io.FileInputStream(temp);
                    }
                    String url = "http://" + ip + ":" + porta + "/api/arquivo?n=" + java.net.URLEncoder.encode(nome, "UTF-8") + (abrir ? "&abrir=1" : "");
                    java.net.HttpURLConnection c = (java.net.HttpURLConnection) new java.net.URL(url).openConnection();
                    c.setConnectTimeout(4000);
                    c.setReadTimeout(60000);
                    c.setRequestMethod("POST");
                    c.setDoOutput(true);
                    c.setRequestProperty("X-Token", token);
                    c.setRequestProperty("Content-Type", "application/octet-stream");
                    c.setFixedLengthStreamingMode(tam);
                    long enviado = 0; int ultimoPct = -1;
                    try (java.io.OutputStream out = c.getOutputStream()) {
                        byte[] b = new byte[65536]; int k;
                        while ((k = in.read(b)) > 0) {
                            out.write(b, 0, k); enviado += k;
                            int pct = tam > 0 ? (int) (enviado * 100 / tam) : 0;
                            if (pct != ultimoPct && (pct - ultimoPct >= 5 || pct == 100)) {
                                ultimoPct = pct;
                                avisarEnvio(new JSONObject().put("i", idx + 1).put("de", lista.size()).put("nome", nome).put("pct", pct));
                            }
                        }
                    }
                    in.close();
                    int codigo = c.getResponseCode();
                    c.disconnect();
                    if (codigo == 200) ok++;
                } catch (Exception e) {
                    try { avisarEnvio(new JSONObject().put("erro", nome)); } catch (Exception ignorar) { }
                } finally {
                    if (temp != null) temp.delete();
                }
            }
            try { avisarEnvio(new JSONObject().put("fim", true).put("ok", ok).put("total", lista.size())); } catch (Exception ignorar) { }
        }).start();
    }

    // ------------------------------------------------------------ tema das barras do sistema

    @SuppressWarnings("deprecation")
    private void aplicarTema(boolean claro) {
        int cor = Color.parseColor(claro ? "#f4f4f6" : "#111114");
        getWindow().setStatusBarColor(cor);
        getWindow().setNavigationBarColor(cor);
        web.setBackgroundColor(cor);
        int flags = claro ? (View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR | View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR) : 0;
        getWindow().getDecorView().setSystemUiVisibility(flags);
    }

    @Override
    public void onRequestPermissionsResult(int codigo, String[] permissoes, int[] resultados) {
        super.onRequestPermissionsResult(codigo, permissoes, resultados);
        web.post(() -> web.evaluateJavascript("window.cuAtualizarNotif && window.cuAtualizarNotif()", null));
    }

    private boolean ehVolume(int codigo) {
        return codigo == KeyEvent.KEYCODE_VOLUME_UP || codigo == KeyEvent.KEYCODE_VOLUME_DOWN;
    }

    @Override
    public boolean onKeyDown(int codigo, KeyEvent evento) {
        if (emControle && ehVolume(codigo)) {
            final String tecla = codigo == KeyEvent.KEYCODE_VOLUME_UP ? "volup" : "voldown";
            web.post(() -> web.evaluateJavascript("window.cuTecla && window.cuTecla('" + tecla + "')", null));
            return true;
        }
        return super.onKeyDown(codigo, evento);
    }

    @Override
    public boolean onKeyUp(int codigo, KeyEvent evento) {
        if (emControle && ehVolume(codigo)) return true;
        return super.onKeyUp(codigo, evento);
    }

    @Override
    public void onBackPressed() {
        web.evaluateJavascript("(window.cuVoltar && window.cuVoltar()) ? 'sim' : 'nao'", value -> {
            if (value == null || !value.contains("sim")) finish();
        });
    }

    @Override
    protected void onDestroy() {
        if (web != null) web.destroy();
        super.onDestroy();
    }

    // ------------------------------------------------------------ notificacao de controle

    private void mostrarNotificacao(String nome, String ip, int porta, String token) {
        if (Build.VERSION.SDK_INT >= 33
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 7);
            return;
        }
        SharedPreferences.Editor ed = getSharedPreferences("cu_notif", MODE_PRIVATE).edit();
        ed.putString("ip", ip).putInt("porta", porta).putString("token", token).apply();

        NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) {
            nm.createNotificationChannel(new NotificationChannel(CANAL, "Controle do computador", NotificationManager.IMPORTANCE_LOW));
        }
        Intent abrir = new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent abrirPi = PendingIntent.getActivity(this, 0, abrir, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);

        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, CANAL) : new Notification.Builder(this);
        b.setSmallIcon(android.R.drawable.ic_media_play)
                .setContentTitle(nome)
                .setContentText("Controle Universal")
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setContentIntent(abrirPi)
                .setColor(Color.parseColor("#e8590c"));
        b.addAction(acao(1, android.R.drawable.ic_lock_silent_mode, "Vol −", "voldown"));
        b.addAction(acao(2, android.R.drawable.ic_media_play, "Play/Pausa", "playpause"));
        b.addAction(acao(4, android.R.drawable.ic_lock_silent_mode_off, "Vol +", "volup"));
        nm.notify(ID_NOTIFICACAO, b.build());
    }

    private Notification.Action acao(int codigo, int icone, String titulo, String tecla) {
        Intent i = new Intent(this, AcaoReceiver.class).setAction("cu.tecla." + tecla).putExtra("tecla", tecla);
        PendingIntent pi = PendingIntent.getBroadcast(this, codigo, i, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        return new Notification.Action.Builder(Icon.createWithResource(this, icone), titulo, pi).build();
    }

    private void esconderNotificacao() {
        ((NotificationManager) getSystemService(NOTIFICATION_SERVICE)).cancel(ID_NOTIFICACAO);
    }

    // ------------------------------------------------------------ atalho na tela inicial

    private boolean fixarAtalho(String pcId, String siteId, String titulo, String cor) {
        try {
            ShortcutManager sm = getSystemService(ShortcutManager.class);
            if (sm == null || !sm.isRequestPinShortcutSupported()) return false;
            int d = 192;
            Bitmap bmp = Bitmap.createBitmap(d, d, Bitmap.Config.ARGB_8888);
            Canvas c = new Canvas(bmp);
            Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
            int fundo;
            try { fundo = Color.parseColor(cor); } catch (Exception e) { fundo = Color.parseColor("#e8590c"); }
            p.setColor(fundo);
            c.drawCircle(d / 2f, d / 2f, d / 2f, p);
            p.setColor(Color.WHITE);
            p.setTextSize(d * 0.55f);
            p.setTextAlign(Paint.Align.CENTER);
            p.setFakeBoldText(true);
            String letra = titulo.isEmpty() ? "?" : titulo.substring(0, 1).toUpperCase();
            c.drawText(letra, d / 2f, d / 2f - (p.descent() + p.ascent()) / 2f, p);

            Intent i = new Intent(Intent.ACTION_MAIN).setClass(this, MainActivity.class)
                    .putExtra("pc", pcId).putExtra("site", siteId);
            ShortcutInfo info = new ShortcutInfo.Builder(this, "site-" + pcId + "-" + siteId)
                    .setShortLabel(titulo).setLongLabel(titulo + " na TV")
                    .setIcon(Icon.createWithBitmap(bmp)).setIntent(i).build();
            return sm.requestPinShortcut(info, null);
        } catch (Exception e) {
            return false;
        }
    }

    // ------------------------------------------------------------ Wake-on-LAN

    private void ligarPc(final String mac) {
        new Thread(() -> {
            try {
                String[] partes = mac.split("[:-]");
                if (partes.length != 6) return;
                byte[] m = new byte[6];
                for (int k = 0; k < 6; k++) m[k] = (byte) Integer.parseInt(partes[k], 16);
                byte[] pacote = new byte[6 + 16 * 6];
                for (int k = 0; k < 6; k++) pacote[k] = (byte) 0xFF;
                for (int k = 0; k < 16; k++) System.arraycopy(m, 0, pacote, 6 + k * 6, 6);
                DatagramSocket s = new DatagramSocket();
                s.setBroadcast(true);
                java.util.List<InetAddress> destinos = new java.util.ArrayList<>();
                destinos.add(InetAddress.getByName("255.255.255.255"));
                for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                    if (!ni.isUp() || ni.isLoopback()) continue;
                    for (InterfaceAddress ia : ni.getInterfaceAddresses()) {
                        if (ia.getBroadcast() != null) destinos.add(ia.getBroadcast());
                    }
                }
                for (InetAddress dst : destinos) {
                    for (int porta : new int[]{9, 7}) {
                        try { s.send(new DatagramPacket(pacote, pacote.length, dst, porta)); } catch (Exception ignorar) { }
                    }
                }
                s.close();
            } catch (Exception ignorar) { }
        }).start();
    }

    // ------------------------------------------------------------ funcoes que a tela HTML pode chamar

    private class Ponte {
        /** Enderecos IPv4 deste celular na rede, como JSON: ["192.168.0.5"]. */
        @JavascriptInterface
        public String ips() {
            StringBuilder sb = new StringBuilder("[");
            try {
                boolean primeiro = true;
                for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                    if (!ni.isUp() || ni.isLoopback()) continue;
                    for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                        if (a instanceof Inet4Address && !a.isLoopbackAddress() && !a.isLinkLocalAddress()) {
                            if (!primeiro) sb.append(',');
                            sb.append('"').append(a.getHostAddress()).append('"');
                            primeiro = false;
                        }
                    }
                }
            } catch (Exception ignorar) { }
            return sb.append(']').toString();
        }

        @JavascriptInterface
        public void emControle(boolean valor) {
            emControle = valor;
        }

        @JavascriptInterface
        public void vibra(int ms) {
            try {
                Vibrator v = (Vibrator) getSystemService(VIBRATOR_SERVICE);
                if (v == null) return;
                if (Build.VERSION.SDK_INT >= 26) v.vibrate(VibrationEffect.createOneShot(ms, VibrationEffect.DEFAULT_AMPLITUDE));
                else v.vibrate(ms);
            } catch (Exception ignorar) { }
        }

        /** Devolve (e limpa) o link compartilhado ou o atalho que abriu o app. */
        @JavascriptInterface
        public String pegarPendente() {
            String p = pendente;
            pendente = "";
            return p;
        }

        @JavascriptInterface
        public void copiar(final String texto) {
            runOnUiThread(() -> {
                ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                if (cm != null) cm.setPrimaryClip(ClipData.newPlainText("Controle Universal", texto));
            });
        }

        @JavascriptInterface
        public String colar() {
            try {
                ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
                if (cm != null && cm.hasPrimaryClip() && cm.getPrimaryClip().getItemCount() > 0) {
                    CharSequence t = cm.getPrimaryClip().getItemAt(0).coerceToText(MainActivity.this);
                    return t == null ? "" : t.toString();
                }
            } catch (Exception ignorar) { }
            return "";
        }

        @JavascriptInterface
        public void voz() {
            runOnUiThread(() -> {
                Intent i = new Intent(android.speech.RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
                i.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE_MODEL, android.speech.RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
                i.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE, "pt-BR");
                i.putExtra(android.speech.RecognizerIntent.EXTRA_PROMPT, "Fale agora");
                try {
                    startActivityForResult(i, PEDIDO_VOZ);
                } catch (Exception e) {
                    web.evaluateJavascript("window.cuVoz && window.cuVoz(null)", null);
                }
            });
        }

        @JavascriptInterface
        public int arquivosPendentes() {
            return arquivosPendentes.size();
        }

        @JavascriptInterface
        public void enviarArquivos(String ip, int porta, String token, boolean abrir) {
            MainActivity.this.enviarArquivos(ip, porta, token, abrir);
        }

        /** Baixa um arquivo do PC para a pasta Downloads do celular (com aviso na barra). */
        @JavascriptInterface
        public boolean baixar(String url, String nome) {
            try {
                android.app.DownloadManager dm = (android.app.DownloadManager) getSystemService(DOWNLOAD_SERVICE);
                android.app.DownloadManager.Request rq = new android.app.DownloadManager.Request(Uri.parse(url))
                        .setTitle(nome)
                        .setNotificationVisibility(android.app.DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                        .setDestinationInExternalPublicDir(android.os.Environment.DIRECTORY_DOWNLOADS, "Controle Universal/" + nome);
                dm.enqueue(rq);
                return true;
            } catch (Exception e) {
                return false;
            }
        }

        @JavascriptInterface
        public void tema(final boolean claro) {
            runOnUiThread(() -> aplicarTema(claro));
        }

        @JavascriptInterface
        public void ligarPc(String mac) {
            MainActivity.this.ligarPc(mac);
        }

        @JavascriptInterface
        public void notificacao(final boolean ligar, final String nome, final String ip, final int porta, final String token) {
            runOnUiThread(() -> {
                if (ligar) mostrarNotificacao(nome, ip, porta, token);
                else esconderNotificacao();
            });
        }

        @JavascriptInterface
        public boolean fixarAtalho(String pcId, String siteId, String titulo, String cor) {
            return MainActivity.this.fixarAtalho(pcId, siteId, titulo, cor);
        }
    }
}

package com.controleuniversal.app;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;

import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;

/** Botoes da notificacao: manda a tecla direto ao computador (POST /api/cmd), sem abrir o app. */
public class AcaoReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context ctx, Intent intent) {
        final String tecla = intent.getStringExtra("tecla");
        if (tecla == null) return;
        SharedPreferences sp = ctx.getSharedPreferences("cu_notif", Context.MODE_PRIVATE);
        final String ip = sp.getString("ip", null);
        final int porta = sp.getInt("porta", 8765);
        final String token = sp.getString("token", null);
        if (ip == null || token == null) return;
        final PendingResult pr = goAsync();
        new Thread(() -> {
            try {
                HttpURLConnection c = (HttpURLConnection) new URL("http://" + ip + ":" + porta + "/api/cmd").openConnection();
                c.setConnectTimeout(2500);
                c.setReadTimeout(2500);
                c.setRequestMethod("POST");
                c.setDoOutput(true);
                c.setRequestProperty("X-Token", token);
                byte[] corpo = ("k " + tecla).getBytes("UTF-8");
                try (OutputStream os = c.getOutputStream()) { os.write(corpo); }
                c.getResponseCode();
                c.disconnect();
            } catch (Exception ignorar) {
            } finally {
                pr.finish();
            }
        }).start();
    }
}

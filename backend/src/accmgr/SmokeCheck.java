package accmgr;

import com.google.gson.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Interactive loopback smoke check. Only deletes the test IDs it just created. */
public final class SmokeCheck {
    private final int port;
    private String token;

    SmokeCheck(int port) { this.port = port; }

    public static void main(String[] args) {
        try {
            if (args.length > 1) throw new IllegalArgumentException();
            int port = args.length == 0 ? 18082 : Integer.parseInt(args[0]);
            if (port < 1 || port > 65535) throw new IllegalArgumentException();
            Console console = System.console();
            if (console == null) throw new IllegalStateException();
            String username = console.readLine("Administrator username: ");
            char[] chars = console.readPassword("Administrator password: ");
            if (username == null || chars == null) throw new IllegalArgumentException();
            try { new SmokeCheck(port).run(username, new String(chars)); }
            finally { Arrays.fill(chars, '\0'); }
        } catch (Exception e) {
            System.err.println("FAIL smoke check: " + e.getClass().getSimpleName());
            System.exit(1);
        }
    }

    void run(String username, String password) throws Exception {
        String platform = null;
        String account = null;
        boolean cleanupFailed = false;
        try {
            token = call("POST", "/api/login", Service.object("username", username, "password", password)).getAsJsonObject().get("token").getAsString();
            platform = call("POST", "/api/platforms", Service.object("name", "兼容验证-" + UUID.randomUUID())).getAsJsonObject().get("id").getAsString();
            account = call("POST", "/api/accounts", Service.object("platform_id", platform, "login", "fictional@example.invalid",
                    "name", "中文测试🔐", "password", "虚构测试密码🔐", "notes", "Temporary compatibility test"))
                    .getAsJsonObject().get("id").getAsString();
            JsonObject detail = call("GET", "/api/accounts/" + account, null).getAsJsonObject();
            if (!"中文测试🔐".equals(detail.get("name").getAsString()) || detail.has("password") || detail.has("password_cipher"))
                throw new IllegalStateException();
            call("POST", "/api/logout", null);
            token = null;
            token = call("POST", "/api/login", Service.object("username", username, "password", password)).getAsJsonObject().get("token").getAsString();
            String revealed = call("POST", "/api/accounts/" + account + "/password", null).getAsJsonObject().get("password").getAsString();
            if (!"虚构测试密码🔐".equals(revealed)) throw new IllegalStateException();
            System.out.println("PASS login, MySQL write/read, Unicode and password decryption after re-login");
        } finally {
            if (account != null) {
                try { call("DELETE", "/api/accounts/" + account, null); }
                catch (Exception e) { cleanupFailed = true; System.err.println("Cleanup pending for test account: " + account); }
            }
            if (platform != null) {
                try { call("DELETE", "/api/platforms/" + platform, null); }
                catch (Exception e) { cleanupFailed = true; System.err.println("Cleanup pending for test platform: " + platform); }
            }
            if (token != null) {
                try { call("POST", "/api/logout", null); }
                catch (Exception e) { cleanupFailed = true; System.err.println("Test session logout failed"); }
            }
            if (cleanupFailed) throw new IllegalStateException();
        }
        System.out.println("PASS test records removed and test session logged out");
    }

    private JsonElement call(String method, String path, Object body) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL("http://127.0.0.1:" + port + path).openConnection();
        c.setRequestMethod(method); c.setConnectTimeout(5000); c.setReadTimeout(30000);
        if (token != null) c.setRequestProperty("Authorization", "Bearer " + token);
        try {
            if (body != null) {
                c.setDoOutput(true); c.setRequestProperty("Content-Type", "application/json");
                try (OutputStream out = c.getOutputStream()) { out.write(new Gson().toJson(body).getBytes(StandardCharsets.UTF_8)); }
            }
            int status = c.getResponseCode();
            if (status != 200) {
                System.err.println("HTTP " + status + " for " + method + " " + path);
                throw new IOException();
            }
            try (Reader in = new InputStreamReader(c.getInputStream(), StandardCharsets.UTF_8)) { return JsonParser.parseReader(in); }
        } finally { c.disconnect(); }
    }
}

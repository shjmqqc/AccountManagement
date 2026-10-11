package accmgr;

import com.google.gson.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.sql.*;
import java.util.*;
import java.util.concurrent.*;
import static accmgr.Database.*;

/** Exercises real HTTP routes and transactions; H2 is not a MySQL 5.5 certification. */
public final class AdminTest {
    private static final Gson JSON = new Gson();
    private static int checks, port;
    private static Database db;
    private static String admin;

    public static void main(String[] args) throws Exception {
        Class.forName("org.h2.Driver");
        db = new Database(() -> DriverManager.getConnection(
                "jdbc:h2:mem:admin;MODE=MySQL;DATABASE_TO_LOWER=TRUE;DB_CLOSE_DELAY=-1;LOCK_TIMEOUT=10000", "sa", ""));
        try (Connection c = db.connections.open(); Statement s = c.createStatement()) {
            for (String file : new String[]{"002-core-schema.sql", "003-admin-schema.sql", "004-sharing-tags.sql"}) {
                String ddl = new String(Files.readAllBytes(Paths.get("../database/" + file)), StandardCharsets.UTF_8)
                        .replaceAll("(?m)^--.*$", "").replace("USE `account_management`;", "")
                        .replaceAll(" CHARACTER SET [a-zA-Z0-9_]+", "").replaceAll(" COLLATE[= ]+[a-zA-Z0-9_]+", "")
                        .replaceAll(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", "");
                for (String sql : ddl.split(";")) if (!sql.trim().isEmpty()) s.execute(sql);
            }
        }
        Service service = new Service(db, new Crypto(Crypto.random(32)));
        service.bootstrap("admin", "123456");
        try (ApiServer server = new ApiServer(service, 0)) {
            server.start(); port = server.port();
            admin = login("admin", "123456");
            flow();
        }
        System.out.println("PASS " + checks + " admin checks (HTTP + H2 transactions, including concurrent invitation use).");
    }

    private static void flow() throws Exception {
        request("GET", "/api/admin/users", null, null, 401);
        JsonObject invite = invite();
        String code = str(invite, "invite_code");
        check(invite.get("expires_at").getAsLong() > System.currentTimeMillis() + Service.SESSION_MILLIS - 10000, "Seven-day expiry");
        request("POST", "/api/register", null, body("invite_code", code, "username", "同事", "password", "12345"), 400);
        request("POST", "/api/register", null, body("invite_code", code, "username", "  ", "password", "123456"), 400);
        request("POST", "/api/register", null, body("invite_code", code, "username", "admin", "password", "123456"), 409);
        String owner = str(request("POST", "/api/register", null,
                body("invite_code", code, "username", "同事", "password", "abcdef", "role", "admin"), 200), "id");
        String user = login("同事", "abcdef"), user2 = login("同事", "abcdef");
        check(str(request("GET", "/api/me", user, null, 200), "role").equals("user"), "Registration cannot promote role");
        request("POST", "/api/register", null, body("invite_code", code, "username", "again", "password", "123456"), 400);
        request("POST", "/api/admin/invitations/" + str(invite, "id") + "/revoke", admin, null, 409);
        for (String path : new String[]{"users", "invitations", "audit-logs", "users/" + owner + "/accounts"})
            request("GET", "/api/admin/" + path, user, null, 403);
        request("POST", "/api/admin/invitations", user, null, 403);
        request("POST", "/api/admin/users/" + owner + "/reset-password", user, null, 403);
        request("POST", "/api/admin/users/" + owner + "/status", user, body("active", false), 403);

        JsonObject revoked = invite();
        request("POST", "/api/admin/invitations/" + str(revoked, "id") + "/revoke", admin, null, 200);
        request("POST", "/api/admin/invitations/" + str(revoked, "id") + "/revoke", admin, null, 200);
        register(str(revoked, "invite_code"), "revoked", 400);
        JsonObject expired = invite();
        try (Connection c = db.connections.open()) { update(c, "UPDATE am_invitations SET expires_at=0 WHERE id=?", str(expired, "id")); }
        register(str(expired, "invite_code"), "expired", 400);
        register(Crypto.token(), "unknown", 400);
        JsonArray invitations = request("GET", "/api/admin/invitations", admin, null, 200).getAsJsonArray();
        Set<String> states = new HashSet<>();
        for (JsonElement item : invitations) states.add(str(item, "status"));
        check(states.containsAll(Arrays.asList("used", "revoked", "expired")), "Invitation states");
        check(!invitations.toString().contains(code) && !invitations.toString().contains("code_hash"), "Invitation list excludes credentials");
        JsonArray firstPage = request("GET", "/api/admin/invitations?limit=1", admin, null, 200).getAsJsonArray();
        check(firstPage.size() == 1, "Invitation page limit");
        JsonArray nextPage = request("GET", "/api/admin/invitations?limit=1&before_id=" + str(firstPage.get(0), "id"), admin, null, 200).getAsJsonArray();
        check(nextPage.size() == 1 && !str(firstPage.get(0), "id").equals(str(nextPage.get(0), "id")), "Invitation cursor");
        request("GET", "/api/admin/invitations?limit=201", admin, null, 400);
        request("GET", "/api/admin/audit-logs?limit=x", admin, null, 400);
        request("GET", "/api/admin/audit-logs?before_id=" + UUID.randomUUID(), admin, null, 400);

        String platform = str(request("POST", "/api/platforms", user, body("name", "测试平台🔐"), 200), "id");
        String account = str(request("POST", "/api/accounts", user,
                body("platform_id", platform, "login", "member", "password", "保存的秘密🔐", "notes", "unique-note"), 200), "id");
        String prefix = "/api/admin/users/" + owner;
        JsonArray users = request("GET", "/api/admin/users", admin, null, 200).getAsJsonArray();
        check(users.size() == 2 && !users.toString().contains("password_hash"), "Safe user listing");
        JsonObject platforms = request("GET", prefix + "/platforms", admin, null, 200).getAsJsonObject();
        check(str(platforms.get("user"), "id").equals(owner) && platforms.getAsJsonArray("data").size() == 1, "Read carries owner identity");
        request("GET", prefix + "/platforms/" + platform, admin, null, 200);
        check(request("GET", prefix + "/accounts?q=unique-note", admin, null, 200).getAsJsonObject().getAsJsonArray("data").size() == 1, "Admin search");
        check(request("GET", prefix + "/accounts?platform_id=" + UUID.randomUUID(), admin, null, 200).getAsJsonObject().getAsJsonArray("data").size() == 0, "Admin filter");
        JsonObject detail = request("GET", prefix + "/accounts/" + account, admin, null, 200).getAsJsonObject();
        check(!detail.toString().contains("password"), "Admin detail excludes ciphertext and plaintext");
        for (String action : new String[]{"view", "copy"})
            check(str(request("POST", prefix + "/accounts/" + account + "/password", admin, body("action", action), 200)
                    .getAsJsonObject().get("data"), "password").equals("保存的秘密🔐"), "Admin explicit password " + action);
        request("POST", prefix + "/accounts/" + account + "/password", user, body("action", "view"), 403);
        request("POST", prefix + "/accounts/" + account + "/password", admin, body("action", "evil"), 400);
        request("POST", prefix + "/accounts/" + account + "/password", admin, null, 400);
        for (String method : new String[]{"POST", "PUT", "DELETE"}) {
            request(method, prefix + "/accounts", admin, body("login", "tamper"), 405);
            request(method, prefix + "/accounts/" + account, admin, body("login", "tamper"), 405);
            request(method, prefix + "/platforms/" + platform, admin, body("name", "tamper"), 405);
        }
        request("PUT", "/api/accounts/" + account, admin, body("platform_id", platform, "login", "tamper"), 404);
        request("DELETE", "/api/accounts/" + account, admin, null, 404);
        request("POST", "/api/accounts/" + account + "/password", admin, null, 404);
        request("GET", "/api/admin/users/" + Service.ADMIN_ID + "/accounts/" + account, admin, null, 404);
        request("POST", "/api/admin/users/" + Service.ADMIN_ID + "/status", admin, body("active", false), 409);
        request("POST", "/api/admin/users/" + Service.ADMIN_ID + "/reset-password", admin, null, 409);
        request("POST", prefix + "/status", admin, body("active", "false"), 400);
        request("POST", prefix + "/status", admin, body("active", false), 200);
        for (String token : new String[]{user, user2}) request("GET", "/api/me", token, null, 401);
        request("POST", "/api/login", null, body("username", "同事", "password", "abcdef"), 401);
        request("GET", prefix + "/accounts", admin, null, 200);
        String disabledTemporary = str(request("POST", prefix + "/reset-password", admin, null, 200), "temporary_password");
        request("POST", "/api/login", null, body("username", "同事", "password", disabledTemporary), 401);
        request("POST", prefix + "/status", admin, body("active", true), 200);
        request("GET", "/api/me", user, null, 401);
        user = login("同事", disabledTemporary); user2 = login("同事", disabledTemporary);
        try (Connection c = db.connections.open()) {
            update(c, "UPDATE am_users SET failed_logins=5,locked_until=? WHERE id=?", System.currentTimeMillis() + 600000, owner);
        }
        String temporary = str(request("POST", prefix + "/reset-password", admin, null, 200), "temporary_password");
        for (String token : new String[]{user, user2}) request("GET", "/api/me", token, null, 401);
        request("POST", "/api/login", null, body("username", "同事", "password", "abcdef"), 401);
        JsonObject resetLogin = request("POST", "/api/login", null, body("username", "同事", "password", temporary), 200).getAsJsonObject();
        check(resetLogin.get("must_change_password").getAsBoolean(), "Temporary password flag");
        user = str(resetLogin, "token"); user2 = login("同事", temporary);
        request("GET", "/api/accounts", user, null, 403);
        request("GET", "/api/me", user, null, 403);
        request("GET", "/api/admin/users", user, null, 403);
        request("POST", "/api/password", user, body("old_password", temporary, "new_password", temporary), 400);
        request("POST", "/api/password", user, body("old_password", temporary, "new_password", "newpass"), 200);
        for (String token : new String[]{user, user2}) request("GET", "/api/me", token, null, 401);
        user = login("同事", "newpass");
        check(str(request("POST", "/api/accounts/" + account + "/password", user, null, 200), "password").equals("保存的秘密🔐"), "Reset preserves vault decryption");
        JsonArray audit = request("GET", "/api/admin/audit-logs?limit=200", admin, null, 200).getAsJsonArray();
        Set<String> actions = new HashSet<>(); boolean failed = false;
        for (JsonElement entry : audit) { actions.add(str(entry, "action")); failed |= str(entry, "outcome").equals("failure"); }
        check(actions.containsAll(Arrays.asList("password_view", "password_copy", "password_reset", "user_disable", "user_enable")) && failed, "Audits successes and failures");
        check(!audit.toString().contains(temporary) && !audit.toString().contains(code) && !audit.toString().contains("保存的秘密"), "Audit excludes secrets");
        try (Connection c = db.connections.open()) {
            check(!rows(c, "SELECT * FROM am_users").toString().contains(temporary), "Temporary password not stored in plaintext");
            check(!rows(c, "SELECT * FROM am_invitations").toString().contains(code), "Invitation not stored in plaintext");
        }
        concurrentRegistration();
        auditFailureRollback(prefix, user, account);
    }

    private static void concurrentRegistration() throws Exception {
        String code = str(invite(), "invite_code");
        ExecutorService pool = Executors.newFixedThreadPool(2);
        CountDownLatch start = new CountDownLatch(1);
        try {
            List<Future<Integer>> calls = new ArrayList<>();
            for (String name : new String[]{"race-one", "race-two"}) calls.add(pool.submit(() -> {
                start.await();
                return call("POST", "/api/register", null, body("invite_code", code, "username", name, "password", "123456")).status;
            }));
            start.countDown();
            Set<Integer> statuses = new HashSet<>();
            for (Future<Integer> call : calls) statuses.add(call.get(30, TimeUnit.SECONDS));
            check(statuses.equals(new HashSet<>(Arrays.asList(200, 400))), "Exactly one concurrent invitation registration wins");
            try (Connection c = db.connections.open()) {
                check(rows(c, "SELECT id FROM am_users WHERE username IN ('race-one','race-two')").size() == 1, "Losing registration leaves no user");
            }
        } finally { pool.shutdownNow(); }
    }

    private static void auditFailureRollback(String prefix, String user, String account) throws Exception {
        // A broken audit store must roll back management writes and withhold secrets.
        try (Connection c = db.connections.open()) { update(c, "ALTER TABLE am_audit_logs RENAME TO audit_unavailable"); }
        try {
            request("POST", prefix + "/reset-password", admin, null, 500);
            request("POST", prefix + "/status", admin, body("active", false), 500);
            request("POST", prefix + "/accounts/" + account + "/password", admin, body("action", "view"), 500);
            request("GET", "/api/me", user, null, 200);
            login("同事", "newpass");
        } finally {
            try (Connection c = db.connections.open()) { update(c, "ALTER TABLE audit_unavailable RENAME TO am_audit_logs"); }
        }
    }

    private static JsonObject invite() throws Exception { return request("POST", "/api/admin/invitations", admin, null, 200).getAsJsonObject(); }
    private static void register(String code, String name, int status) throws Exception {
        request("POST", "/api/register", null, body("invite_code", code, "username", name, "password", "123456"), status);
    }
    private static String login(String name, String password) throws Exception {
        return str(request("POST", "/api/login", null, body("username", name, "password", password), 200), "token");
    }
    private static String str(JsonElement element, String field) { return element.getAsJsonObject().get(field).getAsString(); }
    private static JsonObject body(Object... values) { return JSON.toJsonTree(Service.object(values)).getAsJsonObject(); }
    private static JsonElement request(String method, String path, String token, JsonObject body, int status) throws Exception {
        Response response = call(method, path, token, body);
        check(response.status == status, method + " " + path + " expected " + status + " got " + response.status);
        check(response.noStore, "No-store header");
        return response.json;
    }
    private static final class Response { int status; boolean noStore; JsonElement json; }
    private static Response call(String method, String path, String token, JsonObject body) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL("http://127.0.0.1:" + port + path).openConnection();
        connection.setRequestMethod(method); connection.setConnectTimeout(5000); connection.setReadTimeout(30000);
        if (token != null) connection.setRequestProperty("Authorization", "Bearer " + token);
        if (body != null) {
            connection.setDoOutput(true); connection.setRequestProperty("Content-Type", "application/json");
            try (OutputStream stream = connection.getOutputStream()) { stream.write(body.toString().getBytes(StandardCharsets.UTF_8)); }
        }
        Response response = new Response(); response.status = connection.getResponseCode();
        response.noStore = "no-store".equals(connection.getHeaderField("Cache-Control"));
        try (InputStream stream = response.status >= 400 ? connection.getErrorStream() : connection.getInputStream()) {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream(); byte[] buffer = new byte[1024]; int n;
            while ((n = stream.read(buffer)) != -1) bytes.write(buffer, 0, n);
            response.json = JsonParser.parseString(new String(bytes.toByteArray(), StandardCharsets.UTF_8));
        } finally { connection.disconnect(); }
        return response;
    }
    private static void check(boolean passed, String message) { if (!passed) throw new AssertionError(message); checks++; }
}

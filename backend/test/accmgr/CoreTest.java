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

/** HTTP + JDBC integration tests. H2 is test-only, not a MySQL compatibility claim. */
public final class CoreTest {
    private static final Gson JSON = new Gson();
    private static int checks;
    private static int port;
    private static Database db;
    private static Service service;
    private static String userId = UUID.randomUUID().toString();

    public static void main(String[] args) throws Exception {
        Class.forName("org.h2.Driver");
        db = new Database(() -> DriverManager.getConnection("jdbc:h2:mem:core;MODE=MySQL;DATABASE_TO_LOWER=TRUE;DB_CLOSE_DELAY=-1", "sa", ""));
        String ddl = (new String(Files.readAllBytes(Paths.get("../database/002-core-schema.sql")), StandardCharsets.UTF_8)
                + new String(Files.readAllBytes(Paths.get("../database/004-sharing-tags.sql")), StandardCharsets.UTF_8)
                + new String(Files.readAllBytes(Paths.get("../database/005-dingtalk.sql")), StandardCharsets.UTF_8)
                + new String(Files.readAllBytes(Paths.get("../database/006-mobile-search.sql")), StandardCharsets.UTF_8))
                .replaceAll("(?m)^--.*$", "").replace("USE `account_management`;", "")
                .replaceAll(" CHARACTER SET [a-zA-Z0-9_]+", "")
                .replaceAll(" COLLATE[= ]+[a-zA-Z0-9_]+", "")
                .replaceAll(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", "");
        try (Connection c = db.connections.open(); Statement s = c.createStatement()) {
            for (String sql : ddl.split(";")) if (!sql.trim().isEmpty()) s.execute(sql);
        }
        byte[] key = Crypto.random(32);
        Crypto crypto = new Crypto(key);
        String cipher = crypto.encrypt("测试🔐", "owner:record");
        check(crypto.decrypt(cipher, "owner:record").equals("测试🔐"), "Unicode encryption round-trip");
        check(!cipher.equals(crypto.encrypt("测试🔐", "owner:record")), "Random encryption nonce");
        try { crypto.decrypt(cipher, "other:record"); throw new AssertionError("AAD not enforced"); }
        catch (javax.crypto.AEADBadTagException expected) { checks++; }
        try { new Crypto(Crypto.random(32)).decrypt(cipher, "owner:record"); throw new AssertionError("Wrong key accepted"); }
        catch (javax.crypto.AEADBadTagException expected) { checks++; }
        String[] cipherParts = cipher.split("\\.");
        byte[] tampered = Base64.getDecoder().decode(cipherParts[2]);
        tampered[0] ^= 1;
        try { crypto.decrypt(cipherParts[0] + "." + cipherParts[1] + "." + Base64.getEncoder().encodeToString(tampered), "owner:record");
            throw new AssertionError("Tampering accepted"); }
        catch (javax.crypto.AEADBadTagException expected) { checks++; }
        service = new Service(db, crypto);
        service.bootstrap("管理员", "123456");
        try { service.bootstrap("second", "123456"); throw new AssertionError("Second admin accepted"); }
        catch (Service.Problem expected) { check(expected.status == 409, "One-time bootstrap"); }
        try (Connection c = db.connections.open()) {
            update(c, "INSERT INTO am_users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)",
                    userId, "同事", Crypto.hashPassword("abcdef"), "user", System.currentTimeMillis());
        }
        try (ApiServer server = new ApiServer(service, 0)) {
            server.start(); port = server.port();
            runFlow();
            new SmokeCheck(port).run("管理员", "654321");
            try (Connection c = db.connections.open()) {
                check(rows(c, "SELECT id FROM am_platforms WHERE name LIKE '兼容验证-%'").isEmpty(), "Smoke test cleans only its records");
            }
        }
        // A new service instance sees persisted JDBC data, with the same key.
        Service restarted = new Service(db, new Crypto(key));
        try (ApiServer allInterfaces = new ApiServer(service, "0.0.0.0", 0)) {
            check(allInterfaces.address().getAddress().isAnyLocalAddress(), "Configurable wildcard HTTP binding");
            allInterfaces.start(); port = allInterfaces.port();
            request("GET", "/health", null, null, 200);
        }
        try (ApiServer loopback = new ApiServer(service, 0)) {
            check(loopback.address().getAddress().isLoopbackAddress(), "Legacy constructor remains loopback-only");
        }
        try { new ApiServer(service, " ", 0); throw new AssertionError("Empty bind host accepted"); }
        catch (IllegalArgumentException expected) { checks++; }
        Object login = restarted.execute("POST", "/api/login", null, body("username", "管理员", "password", "654321"), Collections.emptyMap());
        String token = (String) ((Map<?, ?>) login).get("token");
        List<?> accounts = (List<?>) restarted.execute("GET", "/api/accounts", token, new JsonObject(), Collections.emptyMap());
        check(!accounts.isEmpty(), "Service restart retains stored accounts");
        System.out.println("PASS " + checks + " checks (Java 8 + HTTP + H2 MySQL mode). Actual MySQL 5.5 persistence remains to verify.");
    }

    private static void runFlow() throws Exception {
        request("GET", "/health", null, null, 200);
        request("GET", "/api/platforms", null, null, 401);
        request("POST", "/api/login", null, body("username", "管理员", "password", "wrong"), 401);
        String admin = login("管理员", "123456");
        String admin2 = login("管理员", "123456");
        String user = login("同事", "abcdef");
        request("GET", "/api/me", admin2, null, 200);
        String platform = request("POST", "/api/platforms", admin, body("name", "阿里云", "url", "https://www.aliyun.com"), 200).getAsJsonObject().get("id").getAsString();
        request("POST", "/api/platforms", admin, body("name", "阿里云"), 409);
        String other = request("POST", "/api/platforms", user, body("name", "阿里云"), 200).getAsJsonObject().get("id").getAsString();
        request("POST", "/api/platforms", user, body("name", "不安全网址", "url", "javascript:alert(1)"), 400);
        request("GET", "/api/platforms/" + platform, user, null, 404);
        request("PUT", "/api/platforms/" + platform, user, body("name", "篡改"), 404);
        request("DELETE", "/api/platforms/" + platform, user, null, 404);
        request("GET", "/api/platforms/" + other, admin, null, 404); // No unimplemented admin bypass.
        String account = request("POST", "/api/accounts", admin,
                body("platform_id", platform, "name", "生产环境", "login", "me@example.com", "password", "秘密🔐", "notes", "账单用途"), 200)
                .getAsJsonObject().get("id").getAsString();
        request("POST", "/api/accounts", admin, body("platform_id", platform, "name", "生产环境", "login", "me@example.com"), 409);
        String sameLogin = request("POST", "/api/accounts", admin, body("platform_id", platform, "name", "测试环境", "login", "me@example.com", "password", "test-secret"), 200).getAsJsonObject().get("id").getAsString();
        check(reveal(admin, sameLogin).equals("test-secret") && reveal(admin, account).equals("秘密🔐"), "Same login different names retain independent passwords");
        request("PUT", "/api/accounts/" + sameLogin, admin, body("platform_id", platform, "name", "生产环境", "login", "me@example.com"), 409);
        request("PUT", "/api/accounts/" + sameLogin, admin, body("platform_id", platform, "name", "测试环境", "login", "me@example.com"), 200);
        request("DELETE", "/api/accounts/" + sameLogin, admin, null, 200);
        ExecutorService writers = Executors.newFixedThreadPool(2);
        final String writerToken = admin;
        CountDownLatch start = new CountDownLatch(1);
        Callable<Integer> createDuplicate = () -> {
            start.await();
            try {
                service.execute("POST", "/api/accounts", writerToken, body("platform_id", platform, "name", "race", "login", "concurrent"), Collections.emptyMap());
                return 200;
            } catch (Service.Problem problem) { return problem.status; }
        };
        try {
            Future<Integer> first = writers.submit(createDuplicate), second = writers.submit(createDuplicate);
            start.countDown();
            List<Integer> results = Arrays.asList(first.get(10, TimeUnit.SECONDS), second.get(10, TimeUnit.SECONDS));
            Collections.sort(results);
            check(results.equals(Arrays.asList(200, 409)), "Concurrent identical account writes permit exactly one record");
        } finally { writers.shutdownNow(); }
        try (Connection c = db.connections.open()) {
            check(rows(c, "SELECT id FROM am_accounts WHERE platform_id=? AND login='concurrent'", platform).size() == 1, "Concurrent duplicate persisted once");
            update(c, "DELETE FROM am_accounts WHERE platform_id=? AND login='concurrent'", platform);
        }
        request("POST", "/api/accounts", user, body("platform_id", platform, "login", "foreign"), 404);
        request("DELETE", "/api/platforms/" + platform, admin, null, 409);
        for (String method : new String[]{"GET", "PUT", "DELETE"})
            request(method, "/api/accounts/" + account, user, method.equals("PUT") ? body("platform_id", other, "login", "foreign") : null, 404);
        request("POST", "/api/accounts/" + account + "/password", user, null, 404);
        JsonObject detail = request("GET", "/api/accounts/" + account, admin, null, 200).getAsJsonObject();
        check(!detail.has("password") && !detail.has("password_cipher"), "Detail excludes password");
        JsonArray listing = request("GET", "/api/accounts", admin, null, 200).getAsJsonArray();
        check(!listing.toString().contains("秘密") && !listing.toString().contains("password_cipher"), "List excludes secrets");
        check(request("GET", "/api/accounts", user, null, 200).getAsJsonArray().size() == 0, "List isolation");
        for (String search : new String[]{"阿里云", "生产", "me@example.com", "账单"}) {
            check(request("GET", "/api/accounts?q=" + URLEncoder.encode(search, "UTF-8") + "&platform_id=" + other,
                    admin, null, 200).getAsJsonArray().size() == 1, "Global search: " + search);
        }
        check(request("GET", "/api/accounts?q=" + URLEncoder.encode("秘密", "UTF-8"), admin, null, 200).getAsJsonArray().size() == 0, "Passwords not searchable");
        check(reveal(admin, account).equals("秘密🔐"), "Reveal exact Unicode password");
        try (Connection c = db.connections.open()) {
            check(!one(c, "SELECT password_cipher FROM am_accounts WHERE id=?", account).get("password_cipher").toString().contains("秘密"), "Encrypted at rest");
            check(!one(c, "SELECT password_hash FROM am_users WHERE id=?", Service.ADMIN_ID).get("password_hash").equals("123456"), "Login password hashed");
            check(one(c, "SELECT token_hash FROM am_sessions WHERE token_hash=?", admin) == null, "Session token not stored in plaintext");
        }
        request("PUT", "/api/accounts/" + account, admin, body("platform_id", other, "login", "move"), 404);
        request("PUT", "/api/accounts/" + account, admin, body("platform_id", platform, "login", "me@example.com", "notes", "first"), 200);
        check(reveal(admin, account).equals("秘密🔐"), "Omitted password preserved");
        request("PUT", "/api/accounts/" + account, admin2, body("platform_id", platform, "login", "me@example.com", "notes", "last", "password", ""), 200);
        check(reveal(admin, account).equals(""), "Empty platform password supported");
        check(request("GET", "/api/accounts/" + account, admin, null, 200).getAsJsonObject().get("notes").getAsString().equals("last"), "Last save wins");
        request("POST", "/api/password", admin, body("old_password", "123456", "new_password", "12345"), 400);
        request("POST", "/api/password", admin, body("old_password", "123456", "new_password", "654321"), 200);
        request("GET", "/api/me", admin, null, 401);
        request("GET", "/api/me", admin2, null, 401);
        admin = login("管理员", "654321");
        check(reveal(admin, account).equals(""), "Password change preserves vault access");
        String deleteMe = request("POST", "/api/accounts", user, body("platform_id", other, "login", "temporary"), 200).getAsJsonObject().get("id").getAsString();
        request("DELETE", "/api/accounts/" + deleteMe, user, null, 200);
        request("DELETE", "/api/platforms/" + other, user, null, 200);
        request("POST", "/api/logout", user, null, 200);
        request("GET", "/api/me", user, null, 401);
        user = login("同事", "abcdef");
        try (Connection c = db.connections.open()) { update(c, "UPDATE am_sessions SET expires_at=0 WHERE user_id=?", userId); }
        request("GET", "/api/me", user, null, 401);
        user = login("同事", "abcdef");
        try (Connection c = db.connections.open()) { update(c, "UPDATE am_users SET active=0 WHERE id=?", userId); }
        request("GET", "/api/me", user, null, 401);
        request("POST", "/api/login", null, body("username", "同事", "password", "abcdef"), 401);
        try (Connection c = db.connections.open()) { update(c, "UPDATE am_users SET active=1,must_change_password=1 WHERE id=?", userId); }
        user = login("同事", "abcdef");
        request("GET", "/api/accounts", user, null, 403);
        request("POST", "/api/password", user, body("old_password", "abcdef", "new_password", "uvwxyz"), 200);
        for (int i = 0; i < 5; i++) request("POST", "/api/login", null, body("username", "同事", "password", "wrong"), 401);
        request("POST", "/api/login", null, body("username", "同事", "password", "uvwxyz"), 401);
        try (Connection c = db.connections.open()) { update(c, "UPDATE am_users SET locked_until=1 WHERE id=?", userId); }
        user = login("同事", "uvwxyz");
        request("GET", "/api/me", user, null, 200);
        request("POST", "/api/platforms", admin, body("name", String.join("", Collections.nCopies(121, "中"))), 400);
        rawRequest("POST", "/api/login", null, "{\"username\":", 400);
        rawRequest("POST", "/api/login", null, "[]", 400);
        rawRequest("POST", "/api/login", null, String.join("", Collections.nCopies(17000, "x")), 413);
        request("GET", "/api/unknown", admin, null, 404);
    }

    private static String login(String username, String password) throws Exception {
        return request("POST", "/api/login", null, body("username", username, "password", password), 200).getAsJsonObject().get("token").getAsString();
    }
    private static String reveal(String token, String account) throws Exception {
        return request("POST", "/api/accounts/" + account + "/password", token, null, 200).getAsJsonObject().get("password").getAsString();
    }
    private static JsonObject body(Object... values) { return JSON.toJsonTree(Service.object(values)).getAsJsonObject(); }
    private static JsonElement request(String method, String path, String token, JsonObject body, int status) throws Exception {
        return rawRequest(method, path, token, body == null ? null : body.toString(), status);
    }
    private static JsonElement rawRequest(String method, String path, String token, String body, int status) throws Exception {
        HttpURLConnection connection = (HttpURLConnection) new URL("http://127.0.0.1:" + port + path).openConnection();
        connection.setRequestMethod(method);
        connection.setConnectTimeout(5000); connection.setReadTimeout(30000);
        if (token != null) connection.setRequestProperty("Authorization", "Bearer " + token);
        if (body != null) {
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json");
            try (OutputStream stream = connection.getOutputStream()) { stream.write(body.getBytes(StandardCharsets.UTF_8)); }
        }
        int actual = connection.getResponseCode();
        try (InputStream stream = actual >= 400 ? connection.getErrorStream() : connection.getInputStream()) {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream(); byte[] buffer = new byte[1024]; int n;
            while ((n = stream.read(buffer)) != -1) bytes.write(buffer, 0, n);
            String response = new String(bytes.toByteArray(), StandardCharsets.UTF_8);
            check(actual == status, method + " " + path + " expected " + status + " got " + actual + " " + (actual >= 400 ? response : ""));
            check("no-store".equals(connection.getHeaderField("Cache-Control")), "No-store response");
            return JsonParser.parseString(response);
        } finally { connection.disconnect(); }
    }
    private static void check(boolean passed, String message) { if (!passed) throw new AssertionError(message); checks++; }
}

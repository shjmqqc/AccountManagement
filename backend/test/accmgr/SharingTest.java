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

public final class SharingTest {
    private static final Gson JSON = new Gson();
    private static int checks, port;
    private static Database db;

    public static void main(String[] args) throws Exception {
        Class.forName("org.h2.Driver");
        db = new Database(() -> DriverManager.getConnection("jdbc:h2:mem:sharing;MODE=MySQL;DATABASE_TO_LOWER=TRUE;DB_CLOSE_DELAY=-1;LOCK_TIMEOUT=10000", "sa", ""));
        try (Connection c = db.connections.open(); Statement s = c.createStatement()) {
            for (String file : new String[]{"002-core-schema.sql", "003-admin-schema.sql", "004-sharing-tags.sql", "005-dingtalk.sql", "006-mobile-search.sql"}) {
                String ddl = new String(Files.readAllBytes(Paths.get("../database/" + file)), StandardCharsets.UTF_8)
                        .replaceAll("(?m)^--.*$", "").replace("USE `account_management`;", "")
                        .replaceAll(" CHARACTER SET [a-zA-Z0-9_]+", "").replaceAll(" COLLATE[= ]+[a-zA-Z0-9_]+", "")
                        .replaceAll(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", "");
                for (String sql : ddl.split(";")) if (!sql.trim().isEmpty()) s.execute(sql);
            }
        }
        Service service = new Service(db, new Crypto(Crypto.random(32)));
        service.bootstrap("admin", "123456");
        try (Connection c = db.connections.open()) {
            for (String name : new String[]{"owner", "recipient", "third"}) update(c,
                    "INSERT INTO am_users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)",
                    UUID.randomUUID().toString(), name, Crypto.hashPassword("123456"), "user", System.currentTimeMillis());
        }
        try (ApiServer server = new ApiServer(service, 0)) { server.start(); port = server.port(); runFlow(); }
        System.out.println("PASS " + checks + " sharing/tag checks (HTTP + H2; actual MySQL upgrade still needs verification).");
    }

    private static void runFlow() throws Exception {
        String owner = login("owner"), recipient = login("recipient"), third = login("third"), admin = login("admin");
        String ownerId = str(get("/api/me", owner), "id"), recipientId = str(get("/api/me", recipient), "id"), thirdId = str(get("/api/me", third), "id");
        request("GET", "/api/shared/accounts", null, null, 401);
        request("GET", "/api/tags", null, null, 401);
        request("GET", "/api/share-recipients", owner, null, 400);
        check(get("/api/share-recipients?q=rec", owner).getAsJsonArray().size() == 0, "Recipient lookup is exact, not enumeration");
        JsonArray match = get("/api/share-recipients?q=recipient", owner).getAsJsonArray();
        check(match.size() == 1 && match.get(0).getAsJsonObject().size() == 3 && match.get(0).getAsJsonObject().get("ding_name").getAsString().isEmpty(), "Lookup returns only id, username and optional DingTalk name");
        check(get("/api/share-recipients?q=owner", owner).getAsJsonArray().size() == 0, "Self not returned");
        String tag = str(post("/api/tags", owner, body("name", "测试帐号🔐", "color", "orange")), "id");
        JsonElement reused = post("/api/tags", owner, body("name", "测试帐号🔐", "color", "red"));
        check(str(reused, "id").equals(tag) && str(reused, "color").equals("orange"), "Same-owner tag reused without changing color");
        String foreignTag = str(post("/api/tags", recipient, body("name", "测试帐号🔐", "color", "blue")), "id");
        check(!foreignTag.equals(tag), "Tag libraries isolated by owner");
        request("POST", "/api/tags", owner, body("name", "bad", "color", "#123456"), 400);
        request("POST", "/api/tags", owner, body("name", "  ", "color", "blue"), 400);
        request("POST", "/api/tags", owner, body("name", String.join("", Collections.nCopies(41, "字")), "color", "blue"), 400);
        String platform = str(post("/api/platforms", owner, body("name", "共享平台", "url", "https://example.com")), "id");
        String account = str(post("/api/accounts", owner, body("platform_id", platform, "login", "shared", "password", "原密码🔐", "tag_ids", Arrays.asList(tag))), "id");
        String hidden = str(post("/api/accounts", owner, body("platform_id", platform, "login", "not-shared")), "id");
        String grants = "/api/accounts/" + account + "/shares", received = "/api/shared/accounts/" + account;
        check(get("/api/accounts?tag_id=" + tag, owner).getAsJsonArray().size() == 1, "Owned tag filter");
        check(get("/api/accounts?q=shared&tag_id=" + tag, owner).getAsJsonArray().size() == 1, "Search and tag intersect");
        check(get("/api/accounts?tag_id=" + foreignTag, owner).getAsJsonArray().size() == 0, "Foreign tag does not match");
        request("PUT", "/api/accounts/" + account, owner, body("platform_id", platform, "login", "changed", "tag_ids", Arrays.asList(foreignTag)), 400);
        check(str(get("/api/accounts/" + account, owner), "login").equals("shared"), "Invalid tags roll back entire account update");
        request("PUT", "/api/accounts/" + account, owner, body("platform_id", platform, "login", "shared", "tag_ids", Collections.nCopies(21, tag)), 400);
        request("PUT", "/api/accounts/" + account, owner, body("platform_id", platform, "login", "shared"), 200);
        check(get("/api/accounts/" + account, owner).getAsJsonObject().getAsJsonArray("tags").size() == 1, "Old client omission preserves tags");
        post(grants, owner, body("recipient_id", recipientId)); post(grants, owner, body("recipient_id", thirdId));
        post(grants, owner, body("recipient_id", recipientId));
        check(get(grants, owner).getAsJsonArray().size() == 2, "Multiple recipients, idempotent grant");
        request("POST", grants, owner, body("recipient_id", ownerId), 400);
        request("POST", grants, owner, body("recipient_id", UUID.randomUUID().toString()), 400);
        request("GET", grants, recipient, null, 404);
        request("POST", grants, recipient, body("recipient_id", thirdId), 404);
        request("POST", grants, admin, body("recipient_id", thirdId), 404);
        request("DELETE", grants + "/" + thirdId, recipient, null, 404);
        JsonArray shared = get("/api/shared/accounts", recipient).getAsJsonArray();
        check(shared.size() == 1 && str(shared.get(0), "owner_username").equals("owner"), "Only granted account, owner named");
        check(!shared.toString().contains("password") && shared.get(0).getAsJsonObject().getAsJsonArray("tags").size() == 1, "Shared list includes tags, not secrets");
        check(get("/api/shared/accounts?tag_id=" + tag, recipient).getAsJsonArray().size() == 1, "Shared tag filter");
        check(get("/api/shared/accounts?tag_id=" + foreignTag, recipient).getAsJsonArray().size() == 0, "Same-name foreign tag not equivalent");
        check(get("/api/shared/accounts?q=missing", recipient).getAsJsonArray().size() == 0, "Shared search");
        check(get("/api/accounts", recipient).getAsJsonArray().size() == 0, "Received accounts not mixed into own list");
        get(received, recipient);
        for (String path : new String[]{"/api/accounts/" + account, "/api/platforms/" + platform, "/api/shared/accounts/" + hidden})
            request("GET", path, recipient, null, 404);
        for (String method : new String[]{"POST", "PUT", "DELETE"}) request(method, received, recipient, body("login", "tamper"), 405);
        request("PUT", "/api/accounts/" + account, recipient, body("platform_id", platform, "login", "tamper"), 404);
        request("POST", received + "/password", recipient, body("action", "evil"), 400);
        for (String action : new String[]{"view", "copy"}) check(str(post(received + "/password", recipient, body("action", action)), "password").equals("原密码🔐"), "Authorized secret " + action);
        request("PUT", "/api/accounts/" + account, owner, body("platform_id", platform, "login", "shared", "password", "更新密码", "tag_ids", Collections.emptyList()), 200);
        check(str(post(received + "/password", third, body("action", "view")), "password").equals("更新密码"), "Shared password is live original");
        check(get(received, recipient).getAsJsonObject().getAsJsonArray("tags").size() == 0, "Tag removal reaches recipients");
        request("DELETE", grants + "/" + recipientId, owner, null, 200);
        request("GET", received, recipient, null, 404);
        request("POST", received + "/password", recipient, body("action", "copy"), 404);
        check(get("/api/shared/accounts", recipient).getAsJsonArray().size() == 0, "Revocation removes list entry");
        get(received, third);
        post("/api/admin/users/" + ownerId + "/status", admin, body("active", false));
        request("GET", received, third, null, 404);
        check(get("/api/shared/accounts", third).getAsJsonArray().size() == 0, "Disabled owner's shares suspended");
        post("/api/admin/users/" + ownerId + "/status", admin, body("active", true)); owner = login("owner");
        get(received, third);
        post("/api/admin/users/" + thirdId + "/status", admin, body("active", false));
        request("POST", grants, owner, body("recipient_id", thirdId), 400);
        request("GET", received, third, null, 401);
        post("/api/admin/users/" + thirdId + "/status", admin, body("active", true)); third = login("third");
        get(received, third);
        // Audit storage failure must not grant access or disclose a password.
        try (Connection c = db.connections.open()) { update(c, "ALTER TABLE am_audit_logs RENAME TO audit_unavailable"); }
        try {
            request("POST", grants, owner, body("recipient_id", recipientId), 500);
            request("POST", received + "/password", third, body("action", "view"), 500);
            request("DELETE", grants + "/" + thirdId, owner, null, 500);
        } finally { try (Connection c = db.connections.open()) { update(c, "ALTER TABLE audit_unavailable RENAME TO am_audit_logs"); } }
        request("GET", received, recipient, null, 404); get(received, third);
        concurrentGrant(owner, grants, recipientId);
        JsonArray logs = get("/api/admin/audit-logs?limit=200", admin).getAsJsonArray();
        Set<String> actions = new HashSet<>(); boolean failed = false;
        for (JsonElement entry : logs) { actions.add(str(entry, "action")); failed |= str(entry, "outcome").equals("failure"); }
        check(actions.containsAll(Arrays.asList("share_grant", "share_revoke", "shared_password_view", "shared_password_copy")) && failed, "Sharing audit coverage");
        check(!logs.toString().contains("更新密码") && !logs.toString().contains("原密码"), "No password content in audit");
        request("DELETE", "/api/accounts/" + account, owner, null, 200);
        request("GET", received, third, null, 404);
        try (Connection c = db.connections.open()) {
            check(rows(c, "SELECT account_id FROM am_account_shares WHERE account_id=?", account).isEmpty(), "Delete cascades grants");
            check(rows(c, "SELECT account_id FROM am_account_tags WHERE account_id=?", account).isEmpty(), "Delete cascades tag attachments");
        }
    }

    private static void concurrentGrant(String owner, String path, String recipient) throws Exception {
        ExecutorService pool = Executors.newFixedThreadPool(2);
        try {
            List<Future<JsonElement>> futures = new ArrayList<>();
            for (int i = 0; i < 2; i++) futures.add(pool.submit(() -> post(path, owner, body("recipient_id", recipient))));
            for (Future<JsonElement> future : futures) future.get(30, TimeUnit.SECONDS);
            check(get(path, owner).getAsJsonArray().size() == 2, "Concurrent grants do not duplicate recipients");
        } finally { pool.shutdownNow(); }
    }
    private static String login(String name) throws Exception { return str(post("/api/login", null, body("username", name, "password", "123456")), "token"); }
    private static JsonElement get(String path, String token) throws Exception { return request("GET", path, token, null, 200); }
    private static JsonElement post(String path, String token, JsonObject body) throws Exception { return request("POST", path, token, body, 200); }
    private static String str(JsonElement value, String field) { return value.getAsJsonObject().get(field).getAsString(); }
    private static JsonObject body(Object... values) { return JSON.toJsonTree(Service.object(values)).getAsJsonObject(); }
    private static JsonElement request(String method, String path, String token, JsonObject body, int status) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL("http://127.0.0.1:" + port + path).openConnection();
        c.setRequestMethod(method); c.setConnectTimeout(5000); c.setReadTimeout(30000);
        if (token != null) c.setRequestProperty("Authorization", "Bearer " + token);
        if (body != null) {
            c.setDoOutput(true); c.setRequestProperty("Content-Type", "application/json");
            try (OutputStream stream = c.getOutputStream()) { stream.write(body.toString().getBytes(StandardCharsets.UTF_8)); }
        }
        int actual = c.getResponseCode();
        try (InputStream stream = actual >= 400 ? c.getErrorStream() : c.getInputStream()) {
            ByteArrayOutputStream bytes = new ByteArrayOutputStream(); byte[] buffer = new byte[1024]; int n;
            while ((n = stream.read(buffer)) != -1) bytes.write(buffer, 0, n);
            check(actual == status, method + " " + path + " expected " + status + " got " + actual);
            check("no-store".equals(c.getHeaderField("Cache-Control")), "No cache");
            return JsonParser.parseString(new String(bytes.toByteArray(), StandardCharsets.UTF_8));
        } finally { c.disconnect(); }
    }
    private static synchronized void check(boolean condition, String message) { if (!condition) throw new AssertionError(message); checks++; }
}

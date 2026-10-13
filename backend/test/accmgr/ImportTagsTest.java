package accmgr;

import com.google.gson.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.sql.*;
import java.util.*;
import java.util.concurrent.*;
import static accmgr.Database.*;
import static accmgr.Service.*;

public final class ImportTagsTest {
    static Database db; static Service service; static int checks;
    static final Gson JSON = new Gson();
    static JsonObject body(Object... values) { return JSON.toJsonTree(object(values)).getAsJsonObject(); }
    static JsonElement call(String method, String path, String token, JsonObject body) throws Exception {
        return JSON.toJsonTree(service.execute(method, path, token, body, Collections.emptyMap()));
    }
    static String str(JsonElement value, String field) { return value.getAsJsonObject().get(field).getAsString(); }
    static JsonElement get(String path, String token) throws Exception { return call("GET", path, token, body()); }
    static void check(boolean value, String label) { if (!value) throw new AssertionError(label); checks++; }
    interface Action { void run() throws Exception; }
    static void rejects(int status, Action action) throws Exception {
        try { action.run(); throw new AssertionError("Expected " + status); } catch (Problem p) { check(p.status == status, p.getMessage()); }
    }
    static JsonObject row(String platform, String name) { return body("platform_name", platform, "platform_url", "https://example.com", "name", name, "login", "00123", "password", " 密码🔐 ", "notes", "导入", "tag_names", Arrays.asList("测试", "正式")); }
    public static void main(String[] args) throws Exception {
        Class.forName("org.h2.Driver");
        db = new Database(() -> DriverManager.getConnection("jdbc:h2:mem:importtags;MODE=MySQL;DATABASE_TO_LOWER=TRUE;DB_CLOSE_DELAY=-1;LOCK_TIMEOUT=10000", "sa", ""));
        try (Connection c = db.connections.open(); Statement s = c.createStatement()) {
            for (String file : new String[]{"002-core-schema.sql", "003-admin-schema.sql", "004-sharing-tags.sql", "005-dingtalk.sql", "006-mobile-search.sql"}) {
                String ddl = new String(Files.readAllBytes(Paths.get("../database/" + file)), StandardCharsets.UTF_8)
                    .replaceAll("(?m)^--.*$", "").replace("USE `account_management`;", "")
                    .replaceAll(" CHARACTER SET [a-zA-Z0-9_]+", "").replaceAll(" COLLATE[= ]+[a-zA-Z0-9_]+", "")
                    .replaceAll(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", "");
                for (String sql : ddl.split(";")) if (!sql.trim().isEmpty()) s.execute(sql);
            }
            update(c, "INSERT INTO am_users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)", UUID.randomUUID().toString(), "other", Crypto.hashPassword("123456"), "user", 1);
        }
        service = new Service(db, new Crypto(Crypto.random(32))); service.bootstrap("owner", "123456");
        String owner = str(call("POST", "/api/login", null, body("username", "owner", "password", "123456")), "token");
        String other = str(call("POST", "/api/login", null, body("username", "other", "password", "123456")), "token");
        String tag = str(call("POST", "/api/tags", owner, body("name", "测试", "color", "red")), "id");
        JsonObject first = row("导入平台", "生产");
        JsonElement created = call("POST", "/api/accounts/import", owner, first);
        String account = str(created, "id"); check(str(created, "status").equals("created"), "First row created");
        check(get("/api/platforms", owner).getAsJsonArray().size() == 1, "Platform automatically created");
        check(str(call("POST", "/api/accounts/" + account + "/password", owner, body()), "password").equals(" 密码🔐 "), "Password Unicode and spaces preserved");
        JsonObject duplicate = row("导入平台", "生产"); duplicate.addProperty("password", "changed"); duplicate.addProperty("platform_url", "https://changed.example"); duplicate.add("tag_names", JSON.toJsonTree(Arrays.asList("不得创建")));
        check(str(call("POST", "/api/accounts/import", owner, duplicate), "status").equals("skipped"), "Duplicate skipped");
        check(str(call("POST", "/api/accounts/" + account + "/password", owner, body()), "password").equals(" 密码🔐 "), "Duplicate never overwrites password");
        check(get("/api/tags", owner).getAsJsonArray().size() == 2, "Duplicate creates no tags");
        check(str(get("/api/platforms", owner).getAsJsonArray().get(0), "url").equals("https://example.com"), "Existing platform URL unchanged");
        call("POST", "/api/accounts/import", owner, row("导入平台", "测试"));
        check(get("/api/accounts", owner).getAsJsonArray().size() == 2, "Same login different name imported");
        try (Connection c = db.connections.open()) {
            check(one(c, "SELECT color FROM am_tags WHERE id=?", tag).get("color").equals("red"), "Existing tag color retained");
        }
        for (JsonElement value : get("/api/tags", owner).getAsJsonArray()) check(value.getAsJsonObject().get("account_count").getAsInt() == 2, "Tag usage counts");
        JsonObject bad = row("应回滚平台", "失败"); bad.addProperty("password", String.join("", Collections.nCopies(1025, "x"))); bad.add("tag_names", JSON.toJsonTree(Arrays.asList("应回滚标签")));
        rejects(400, () -> call("POST", "/api/accounts/import", owner, bad));
        check(get("/api/platforms", owner).getAsJsonArray().size() == 1 && get("/api/tags", owner).getAsJsonArray().size() == 2, "Failed row rolls back platform and tags");
        rejects(404, () -> call("PUT", "/api/tags/" + tag, other, body("name", "越权", "color", "blue")));
        rejects(404, () -> call("DELETE", "/api/tags/" + tag, other, body()));
        rejects(409, () -> call("PUT", "/api/tags/" + tag, owner, body("name", "正式", "color", "blue")));
        rejects(400, () -> call("PUT", "/api/tags/" + tag, owner, body("name", "改名", "color", "invalid")));
        String recipient = str(get("/api/me", other), "id");
        call("POST", "/api/accounts/" + account + "/shares", owner, body("recipient_id", recipient));
        call("PUT", "/api/tags/" + tag, owner, body("name", "生产标签", "color", "green"));
        check(get("/api/shared/accounts", other).toString().contains("生产标签"), "Shared account sees renamed label");
        call("DELETE", "/api/tags/" + tag, owner, body());
        check(!get("/api/accounts", owner).toString().contains("生产标签") && get("/api/accounts", owner).getAsJsonArray().size() == 2, "Delete removes associations, retains accounts");
        check(str(call("POST", "/api/shared/accounts/" + account + "/password", other, body("action", "copy")), "password").equals(" 密码🔐 "), "Delete retains share and password");
        check(str(call("POST", "/api/accounts/import", other, first), "status").equals("created"), "Imports isolated to caller");
        ExecutorService pool = Executors.newFixedThreadPool(2); CountDownLatch gate = new CountDownLatch(1);
        Callable<String> writer = () -> { gate.await(); return str(call("POST", "/api/accounts/import", owner, row("并发平台", "同名")), "status"); };
        try {
            Future<String> a = pool.submit(writer), b = pool.submit(writer); gate.countDown();
            Set<String> statuses = new HashSet<>(Arrays.asList(a.get(10, TimeUnit.SECONDS), b.get(10, TimeUnit.SECONDS)));
            check(statuses.size() == 2 && statuses.contains("created") && statuses.contains("skipped"), "Concurrent import creates once and skips once");
        } finally { pool.shutdownNow(); }
        rejects(405, () -> get("/api/accounts/import", owner));
        System.out.println("PASS " + checks + " import/tag-management checks (H2 transactions).");
    }
}

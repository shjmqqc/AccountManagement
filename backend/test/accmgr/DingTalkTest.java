package accmgr;

import com.google.gson.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.sql.*;
import java.util.*;
import java.util.concurrent.*;
import static accmgr.Database.*;
import static accmgr.Service.*;

public final class DingTalkTest {
    static int checks;
    static Database db;
    static Service service;
    static String profileName = "张三";
    static final Gson JSON = new Gson();
    static final Map<String, String> codes = new ConcurrentHashMap<>();
    static String code(String dingUser) { String code = Crypto.token(); codes.put(code, dingUser); return code; }
    static JsonObject body(Object... values) { return JSON.toJsonTree(object(values)).getAsJsonObject(); }
    static JsonObject call(String method, String path, String token, JsonObject body) throws Exception {
        return JSON.toJsonTree(service.execute(method, path, token, body, Collections.emptyMap())).getAsJsonObject();
    }
    static JsonObject bind(String identity, String user, String password) throws Exception {
        return call("POST", "/api/dingtalk/bind", null, body("code", code(identity), "username", user, "password", password));
    }
    static JsonObject login(String identity) throws Exception { return call("POST", "/api/dingtalk/login", null, body("code", code(identity))); }
    static void check(boolean ok, String message) { if (!ok) throw new AssertionError(message); checks++; }
    interface Action { void run() throws Exception; }
    static void rejects(int status, String error, Action action) throws Exception {
        try { action.run(); throw new AssertionError("Expected " + error); }
        catch (Problem problem) { check(problem.status == status && problem.getMessage().equals(error), "Reject " + error); }
    }
    static void sql(String statement, Object... args) throws Exception { try (Connection c = db.connections.open()) { update(c, statement, args); } }
    static long count(String table) throws Exception { try (Connection c = db.connections.open()) { return ((Number) one(c, "SELECT COUNT(*) AS n FROM " + table).get("n")).longValue(); } }

    public static void main(String[] args) throws Exception {
        Class.forName("org.h2.Driver");
        db = new Database(() -> DriverManager.getConnection("jdbc:h2:mem:ding;MODE=MySQL;DATABASE_TO_LOWER=TRUE;DB_CLOSE_DELAY=-1;LOCK_TIMEOUT=10000", "sa", ""));
        try (Connection c = db.connections.open(); Statement s = c.createStatement()) {
            for (String file : new String[]{"002-core-schema.sql", "003-admin-schema.sql", "004-sharing-tags.sql", "005-dingtalk.sql", "006-mobile-search.sql"}) {
                String ddl = new String(Files.readAllBytes(Paths.get("../database/" + file)), StandardCharsets.UTF_8)
                    .replaceAll("(?m)^--.*$", "").replace("USE `account_management`;", "")
                    .replaceAll(" CHARACTER SET [a-zA-Z0-9_]+", "").replaceAll(" COLLATE[= ]+[a-zA-Z0-9_]+", "")
                    .replaceAll(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", "");
                for (String part : ddl.split(";")) if (!part.trim().isEmpty()) s.execute(part);
            }
        }
        service = new Service(db, new Crypto(Crypto.random(32)));
        service.bootstrap("admin", "123456");
        check(!call("GET", "/api/dingtalk/config", null, body()).get("enabled").getAsBoolean(), "Disabled default");
        rejects(503, "dingtalk_disabled", () -> login("ding-owner"));
        service.enableDingTalk(new DingTalkClient("ding-test", "test-app", "test-secret") {
            @Override String name(String userId) {
                if (profileName == null) throw new Problem(502, "dingtalk_unavailable");
                return profileName;
            }
            @Override String identify(String code) {
                String identity = codes.remove(code);
                if (identity == null) throw new Problem(401, "dingtalk_code_invalid");
                return identity;
            }
        });
        String owner = UUID.randomUUID().toString(), other = UUID.randomUUID().toString();
        sql("INSERT INTO am_users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)", owner, "owner", Crypto.hashPassword("123456"), "user", 1);
        sql("INSERT INTO am_users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)", other, "other", Crypto.hashPassword("123456"), "user", 1);
        String config = call("GET", "/api/dingtalk/config", null, body()).toString();
        check(config.contains("test-app") && !config.contains("test-secret"), "Public config excludes secret");
        check(!login("unknown").get("bound").getAsBoolean() && count("am_users") == 3, "Unbound login does not provision users");
        rejects(401, "dingtalk_code_invalid", () -> call("POST", "/api/dingtalk/login", null, body("code", "fake", "user_id", owner)));
        rejects(401, "invalid_credentials", () -> bind("ding-owner", "owner", "incorrect"));
        check(count("am_dingtalk_bindings") == 0 && count("am_sessions") == 0, "Failed bind does not create session or mapping");
        String token = bind("ding-owner", "owner", "123456").get("token").getAsString();
        check(call("GET", "/api/me", token, body()).get("id").getAsString().equals(owner), "Bind preserves existing user");
        check(call("GET", "/api/me", token, body()).get("role").getAsString().equals("user"), "No privilege escalation");
        check(call("GET", "/api/dingtalk/binding", token, body()).get("bound").getAsBoolean(), "Binding status");
        check(call("GET", "/api/dingtalk/binding", token, body()).get("ding_name").getAsString().equals("张三"), "Full DingTalk name is stored");
        bind("ding-other", "other", "123456");
        List<?> matches = (List<?>) service.execute("GET", "/api/share-recipients", token, body(), Collections.singletonMap("q", "张"));
        check(matches.size() == 1 && matches.toString().contains("other"), "Partial name search excludes self and preserves username");
        bind("ding-admin", "admin", "123456");
        matches = (List<?>) service.execute("GET", "/api/share-recipients", token, body(), Collections.singletonMap("q", "张"));
        check(matches.size() == 2 && matches.toString().contains("admin") && matches.toString().contains("other"), "Same names remain separate recipients with usernames");
        sql("DELETE FROM am_dingtalk_bindings WHERE ding_user_id='ding-admin'");
        sql("UPDATE am_dingtalk_bindings SET corp_id='different-corp' WHERE user_id=?", other);
        check(((List<?>) service.execute("GET", "/api/share-recipients", token, body(), Collections.singletonMap("q", "张"))).isEmpty(), "Name search stays in configured enterprise");
        sql("UPDATE am_dingtalk_bindings SET corp_id='ding-test' WHERE user_id=?", other);
        check(((List<?>) service.execute("GET", "/api/share-recipients", token, body(), Collections.singletonMap("q", "%"))).isEmpty(), "Search treats wildcard characters literally");
        sql("UPDATE am_users SET active=0 WHERE id=?", other);
        check(((List<?>) service.execute("GET", "/api/share-recipients", token, body(), Collections.singletonMap("q", "张"))).isEmpty(), "Name search excludes disabled users");
        sql("UPDATE am_users SET active=1 WHERE id=?", other);
        sql("DELETE FROM am_dingtalk_bindings WHERE user_id=?", other);
        sql("UPDATE am_dingtalk_bindings SET ding_name='' WHERE user_id=?", owner);
        check(call("GET", "/api/dingtalk/binding", token, body()).get("ding_name").getAsString().equals("张三"), "Old binding name backfilled");
        profileName = null;
        check(login("ding-owner").has("token"), "Missing profile permission does not break SSO");
        check(call("GET", "/api/dingtalk/binding", token, body()).get("ding_name").getAsString().equals("张三"), "Failed profile lookup preserves stored name");
        profileName = "张三新名";
        login("ding-owner");
        check(call("GET", "/api/dingtalk/binding", token, body()).get("ding_name").getAsString().equals("张三新名"), "SSO refreshes changed profile name");
        profileName = "张三";
        bind("ding-owner", "owner", "123456"); check(count("am_dingtalk_bindings") == 1, "Idempotent same binding");
        rejects(409, "dingtalk_already_bound", () -> bind("ding-owner", "other", "123456"));
        rejects(409, "dingtalk_already_bound", () -> bind("ding-other", "owner", "123456"));
        String once = code("ding-owner");
        String auto = call("POST", "/api/dingtalk/login", null, body("code", once, "user_id", other)).get("token").getAsString();
        check(call("GET", "/api/me", auto, body()).get("id").getAsString().equals(owner), "Forged frontend user ID ignored");
        rejects(401, "dingtalk_code_invalid", () -> call("POST", "/api/dingtalk/login", null, body("code", once)));
        sql("UPDATE am_users SET active=0 WHERE id=?", owner);
        rejects(401, "dingtalk_login_denied", () -> login("ding-owner"));
        rejects(401, "login_required", () -> call("GET", "/api/me", auto, body()));
        sql("UPDATE am_users SET active=1,must_change_password=1 WHERE id=?", owner);
        long sessions = count("am_sessions");
        rejects(403, "dingtalk_password_required", () -> login("ding-owner"));
        rejects(403, "dingtalk_password_required", () -> bind("ding-owner", "owner", "123456"));
        check(count("am_sessions") == sessions, "Reset password cannot be bypassed by issuing new SSO session");
        String temporary = call("POST", "/api/login", null, body("username", "owner", "password", "123456")).get("token").getAsString();
        rejects(403, "password_change_required", () -> call("GET", "/api/me", temporary, body()));
        call("POST", "/api/password", temporary, body("old_password", "123456", "new_password", "654321"));
        String afterReset = login("ding-owner").get("token").getAsString();
        check(call("GET", "/api/me", afterReset, body()).get("id").getAsString().equals(owner), "SSO works after required password change");
        rejects(401, "invalid_credentials", () -> call("DELETE", "/api/dingtalk/binding", afterReset, body("password", "wrong")));
        check(count("am_dingtalk_bindings") == 1, "Wrong unbind password retains binding");
        call("DELETE", "/api/dingtalk/binding", afterReset, body("password", "654321"));
        check(count("am_dingtalk_bindings") == 0, "Unbind removes mapping");
        rejects(401, "login_required", () -> call("GET", "/api/me", afterReset, body()));
        check(!login("ding-owner").get("bound").getAsBoolean(), "Unbound identity cannot log in");
        for (int i = 0; i < 5; i++) rejects(401, "invalid_credentials", () -> bind("ding-owner", "owner", "wrong"));
        rejects(401, "invalid_credentials", () -> bind("ding-owner", "owner", "654321"));
        sql("UPDATE am_users SET locked_until=0,failed_logins=0 WHERE id=?", owner);
        bind("ding-owner", "owner", "654321");
        sql("UPDATE am_users SET locked_until=? WHERE id=?", System.currentTimeMillis() + 60000, owner);
        rejects(401, "dingtalk_login_denied", () -> login("ding-owner"));
        check(count("am_audit_logs") >= 3, "Binding and unbinding audited");
        System.out.println("PASS " + checks + " DingTalk identity/binding/session checks (mock upstream, real H2 transactions).");
    }
}

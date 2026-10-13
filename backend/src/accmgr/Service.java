package accmgr;

import com.google.gson.*;
import java.net.URI;
import java.sql.*;
import java.util.*;
import static accmgr.Database.*;

final class Service {
    static final long SESSION_MILLIS = 7L * 24 * 60 * 60 * 1000;
    static final String ADMIN_ID = "00000000-0000-0000-0000-000000000001";
    private final Database db;
    private final Crypto crypto;
    private final String dummyHash;
    private long loginWindow;
    private int loginCount;
    private DingTalkAuth dingTalk;

    void enableDingTalk(DingTalkClient client) { dingTalk = new DingTalkAuth(db, this, client); }
    String dingCorpId() { return dingTalk == null ? "" : dingTalk.client.corpId; }

    static final class Problem extends RuntimeException {
        final int status;
        Problem(int status, String code) { super(code); this.status = status; }
    }

    Service(Database db, Crypto crypto) throws Exception {
        this.db = db;
        this.crypto = crypto;
        dummyHash = Crypto.hashPassword(Crypto.token());
    }

    void bootstrap(String username, String password) throws Exception {
        username = checked(username.trim(), 1, 100);
        checked(password, 6, 256);
        try (Connection c = db.connections.open()) {
            if (one(c, "SELECT id FROM am_users WHERE role = 'admin'") != null)
                throw new Problem(409, "admin_already_initialized");
            update(c, "INSERT INTO am_users (id,username,password_hash,role,created_at) VALUES (?,?,?,?,?)",
                    ADMIN_ID, username, Crypto.hashPassword(password), "admin", System.currentTimeMillis());
        }
    }

    Object execute(String method, String path, String token, JsonObject body, Map<String, String> query) throws Exception {
        if (path.equals("/api/dingtalk/config") && method.equals("GET"))
            return dingTalk == null ? object("enabled", false) : object("enabled", true, "corp_id", dingTalk.client.corpId, "client_id", dingTalk.client.clientId);
        if (path.equals("/api/dingtalk/login") || path.equals("/api/dingtalk/bind")) {
            if (!method.equals("POST")) throw new Problem(405, "method_not_allowed");
            if (dingTalk == null) throw new Problem(503, "dingtalk_disabled");
            limitLogin();
            return dingTalk.login(body, path.endsWith("/bind"));
        }
        if (path.equals("/api/dingtalk/binding") && method.equals("DELETE")) limitLogin();
        if (path.equals("/api/login") && method.equals("POST")) {
            limitLogin();
            return login(body);
        }
        if (path.equals("/api/register") && method.equals("POST")) {
            limitLogin();
            return new AdminService(this).register(db, body);
        }
        try (Connection c = db.connections.open()) {
            c.setTransactionIsolation(Connection.TRANSACTION_READ_COMMITTED);
            c.setAutoCommit(false);
            try {
                Map<String, Object> user = authenticate(c, token);
                String owner = (String) user.get("id");
                if (number(user, "must_change_password") != 0
                        && !path.equals("/api/password") && !path.equals("/api/logout"))
                    throw new Problem(403, "password_change_required");
                Object result;
                if (path.startsWith("/api/admin/")) {
                    if (!"admin".equals(user.get("role"))) throw new Problem(403, "admin_required");
                    result = new AdminService(this).execute(c, owner, method, path, body, query);
                } else if (path.equals("/api/dingtalk/binding")) {
                    if (dingTalk == null) throw new Problem(503, "dingtalk_disabled");
                    result = dingTalk.binding(c, user, method, body);
                } else if (path.equals("/api/me") && method.equals("GET")) {
                    result = object("id", owner, "username", user.get("username"), "role", user.get("role"));
                } else if (path.equals("/api/logout") && method.equals("POST")) {
                    update(c, "DELETE FROM am_sessions WHERE token_hash=?", Crypto.digest(token));
                    result = object("ok", true);
                } else if (path.equals("/api/password") && method.equals("POST")) {
                    String oldPassword = text(body, "old_password", 1, 256);
                    String password = text(body, "new_password", 6, 256);
                    if (!Crypto.verifyPassword(oldPassword, (String) user.get("password_hash")))
                        throw new Problem(400, "incorrect_password");
                    if (number(user, "must_change_password") != 0 && oldPassword.equals(password))
                        throw new Problem(400, "new_password_must_differ");
                    update(c, "UPDATE am_users SET password_hash=?,must_change_password=0 WHERE id=?",
                            Crypto.hashPassword(password), owner);
                    update(c, "DELETE FROM am_sessions WHERE user_id=?", owner);
                    result = object("ok", true, "login_required", true);
                } else if (path.equals("/api/share-recipients")) {
                    result = new Sharing(this).recipients(c, owner, method, query);
                } else if (path.equals("/api/tags")) {
                    result = Sharing.tags(c, owner, method, body);
                } else if (path.startsWith("/api/tags/")) {
                    result = Sharing.tag(c, owner, id(path.substring(10)), method, body);
                } else if (path.equals("/api/shared/accounts") || path.startsWith("/api/shared/accounts/")) {
                    result = new Sharing(this).received(c, owner, method,
                            path.equals("/api/shared/accounts") ? "" : path.substring(21), body, query);
                } else if (path.equals("/api/platforms")) {
                    result = platforms(c, owner, method, body);
                } else if (path.startsWith("/api/platforms/")) {
                    result = platform(c, owner, id(path.substring(15)), method, body);
                } else if (path.equals("/api/accounts/import")) {
                    if (!method.equals("POST")) throw new Problem(405, "method_not_allowed");
                    result = importAccount(c, owner, body);
                } else if (path.equals("/api/accounts")) {
                    result = accounts(c, owner, method, body, query);
                } else if (path.startsWith("/api/accounts/")) {
                    String suffix = path.substring(14);
                    String[] parts = suffix.split("/", -1);
                    if ((parts.length == 2 || parts.length == 3) && parts[1].equals("shares")) {
                        result = new Sharing(this).grants(c, owner, id(parts[0]), parts.length == 3 ? id(parts[2]) : null, method, body);
                    } else {
                    boolean reveal = suffix.endsWith("/password");
                    String accountId = id(reveal ? suffix.substring(0, suffix.length() - 9) : suffix);
                    result = account(c, owner, accountId, method, body, reveal);
                    }
                } else {
                    throw new Problem(404, "not_found");
                }
                c.commit();
                return result;
            } catch (Exception e) {
                c.rollback();
                throw e;
            }
        } catch (SQLException e) {
            if (e.getErrorCode() == 1062 || "23505".equals(e.getSQLState()))
                throw new Problem(409, "duplicate_record");
            if (e.getErrorCode() == 1451 || "23503".equals(e.getSQLState()))
                throw new Problem(409, "record_in_use");
            if ("40001".equals(e.getSQLState())) throw new Problem(409, "concurrent_change_retry");
            throw e;
        }
    }

    private synchronized void limitLogin() {
        long now = System.currentTimeMillis();
        if (now - loginWindow >= 60000) { loginWindow = now; loginCount = 0; }
        if (++loginCount > 60) throw new Problem(429, "too_many_login_attempts");
    }

    private Object login(JsonObject body) throws Exception {
        String username = text(body, "username", 1, 100).trim();
        String password = text(body, "password", 1, 256);
        try (Connection c = db.connections.open()) {
            c.setTransactionIsolation(Connection.TRANSACTION_READ_COMMITTED);
            c.setAutoCommit(false);
            try {
                Map<String, Object> user = one(c, "SELECT * FROM am_users WHERE username=? FOR UPDATE", username);
                verifyLoginPassword(c, user, password);
                Object result = newSession(c, user);
                c.commit();
                return result;
            } catch (Exception e) {
                c.rollback();
                throw e;
            }
        }
    }

    void verifyLoginPassword(Connection c, Map<String, Object> user, String password) throws Exception {
        long now = System.currentTimeMillis();
        if (user == null || number(user, "active") == 0 || number(user, "locked_until") > now) {
            Crypto.verifyPassword(password, dummyHash); throw new Problem(401, "invalid_credentials");
        }
        if (!Crypto.verifyPassword(password, (String) user.get("password_hash"))) {
            long failures = number(user, "locked_until") > 0 ? 1 : number(user, "failed_logins") + 1;
            update(c, "UPDATE am_users SET failed_logins=?,locked_until=? WHERE id=?", failures, failures >= 5 ? now + 15 * 60 * 1000 : 0, user.get("id"));
            c.commit(); // Called before business mutations; retain failed attempts on rejection.
            throw new Problem(401, "invalid_credentials");
        }
        update(c, "UPDATE am_users SET failed_logins=0,locked_until=0 WHERE id=?", user.get("id"));
    }

    Object newSession(Connection c, Map<String, Object> user) throws Exception {
        long now = System.currentTimeMillis(), expires = now + SESSION_MILLIS;
        update(c, "DELETE FROM am_sessions WHERE expires_at<=?", now);
        String token = Crypto.token();
        update(c, "INSERT INTO am_sessions (token_hash,user_id,expires_at) VALUES (?,?,?)", Crypto.digest(token), user.get("id"), expires);
        return object("token", token, "expires_at", expires, "must_change_password", number(user, "must_change_password") != 0);
    }

    private Map<String, Object> authenticate(Connection c, String token) throws Exception {
        if (token == null || !token.matches("[A-Za-z0-9_-]{43}")) throw new Problem(401, "login_required");
        Map<String, Object> session = one(c, "SELECT user_id FROM am_sessions WHERE token_hash=? AND expires_at>?",
                Crypto.digest(token), System.currentTimeMillis());
        if (session == null) throw new Problem(401, "login_required");
        // Serialize writes per owner, including password changes and session invalidation.
        Map<String, Object> user = one(c, "SELECT * FROM am_users WHERE id=? FOR UPDATE", session.get("user_id"));
        if (user == null || number(user, "active") == 0) throw new Problem(401, "login_required");
        // Locking read observes logout/password-change commits after waiting for the user lock.
        if (one(c, "SELECT token_hash FROM am_sessions WHERE token_hash=? AND expires_at>? FOR UPDATE",
                Crypto.digest(token), System.currentTimeMillis()) == null) throw new Problem(401, "login_required");
        return user;
    }

    Object platforms(Connection c, String owner, String method, JsonObject body) throws Exception {
        if (method.equals("GET")) return rows(c,
                "SELECT p.id,p.name,p.url,p.created_at,p.updated_at,"
                + "(SELECT COUNT(*) FROM am_accounts a WHERE a.platform_id=p.id) AS account_count "
                + "FROM am_platforms p WHERE p.owner_id=? ORDER BY p.name,p.id", owner);
        if (!method.equals("POST")) throw new Problem(405, "method_not_allowed");
        String platformId = UUID.randomUUID().toString();
        long now = System.currentTimeMillis();
        update(c, "INSERT INTO am_platforms (id,owner_id,name,url,created_at,updated_at) VALUES (?,?,?,?,?,?)",
                platformId, owner, platformName(body), url(body), now, now);
        return object("id", platformId);
    }

    Object platform(Connection c, String owner, String platformId, String method, JsonObject body) throws Exception {
        Map<String, Object> record = ownedPlatform(c, owner, platformId);
        if (method.equals("GET")) return record;
        if (method.equals("PUT")) {
            update(c, "UPDATE am_platforms SET name=?,url=?,updated_at=? WHERE id=? AND owner_id=?",
                    platformName(body), url(body), System.currentTimeMillis(), platformId, owner);
        } else if (method.equals("DELETE")) {
            if (one(c, "SELECT id FROM am_accounts WHERE platform_id=? AND owner_id=?", platformId, owner) != null)
                throw new Problem(409, "platform_not_empty");
            update(c, "DELETE FROM am_platforms WHERE id=? AND owner_id=?", platformId, owner);
        } else throw new Problem(405, "method_not_allowed");
        return object("ok", true);
    }

    private Map<String, Object> ownedPlatform(Connection c, String owner, String platformId) throws Exception {
        Map<String, Object> record = one(c, "SELECT id,name,url,created_at,updated_at FROM am_platforms WHERE id=? AND owner_id=?",
                platformId, owner);
        if (record == null) throw new Problem(404, "not_found");
        return record;
    }

    Object accounts(Connection c, String owner, String method, JsonObject body, Map<String, String> query) throws Exception {
        if (method.equals("GET")) {
            List<Map<String, Object>> results = rows(c,
                    "SELECT a.id,a.platform_id,a.name,a.login,a.notes,a.created_at,a.updated_at,p.name AS platform_name, "
                    + "(SELECT COUNT(*) FROM am_account_shares s WHERE s.account_id=a.id) AS share_count "
                    + "FROM am_accounts a JOIN am_platforms p ON p.id=a.platform_id AND p.owner_id=a.owner_id "
                    + "WHERE a.owner_id=? ORDER BY a.updated_at DESC,a.id", owner);
            Sharing.attachTags(c, results);
            Sharing.filter(results, query);
            return results;
        }
        if (!method.equals("POST")) throw new Problem(405, "method_not_allowed");
        String accountId = UUID.randomUUID().toString();
        saveAccount(c, owner, accountId, body, true, null);
        return object("id", accountId);
    }

    Object account(Connection c, String owner, String accountId, String method, JsonObject body, boolean reveal) throws Exception {
        Map<String, Object> record = one(c,
                "SELECT id,platform_id,name,login,notes,password_cipher,created_at,updated_at "
                + "FROM am_accounts WHERE id=? AND owner_id=?", accountId, owner);
        if (record == null) throw new Problem(404, "not_found");
        String ciphertext = (String) record.remove("password_cipher");
        if (reveal) {
            if (!method.equals("POST")) throw new Problem(405, "method_not_allowed");
            return object("password", crypto.decrypt(ciphertext, owner + ":" + accountId));
        }
        if (method.equals("GET")) { Sharing.attachTags(c, Collections.singletonList(record)); return record; }
        if (method.equals("PUT")) saveAccount(c, owner, accountId, body, false, ciphertext);
        else if (method.equals("DELETE")) update(c, "DELETE FROM am_accounts WHERE id=? AND owner_id=?", accountId, owner);
        else throw new Problem(405, "method_not_allowed");
        return object("ok", true);
    }

    private void saveAccount(Connection c, String owner, String accountId, JsonObject body, boolean create, String previous) throws Exception {
        String platformId = id(text(body, "platform_id", 36, 36));
        ownedPlatform(c, owner, platformId);
        String name = optional(body, "name", 120);
        String login = checked(text(body, "login", 1, 160).trim(), 1, 160);
        String notes = optional(body, "notes", 2000);
        // authenticate holds the owner's row lock until commit, including concurrent creates/updates.
        if (one(c, "SELECT id FROM am_accounts WHERE platform_id=? AND login=? AND name=? AND id<>?",
                platformId, login, name, accountId) != null) throw new Problem(409, "duplicate_record");
        String ciphertext = previous;
        if (create || body.has("password"))
            ciphertext = crypto.encrypt(optional(body, "password", 1024), owner + ":" + accountId);
        long now = System.currentTimeMillis();
        if (create) update(c,
                "INSERT INTO am_accounts (id,owner_id,platform_id,name,login,password_cipher,notes,created_at,updated_at) "
                + "VALUES (?,?,?,?,?,?,?,?,?)", accountId, owner, platformId, name, login, ciphertext, notes, now, now);
        else update(c, "UPDATE am_accounts SET platform_id=?,name=?,login=?,password_cipher=?,notes=?,updated_at=? WHERE id=? AND owner_id=?",
                platformId, name, login, ciphertext, notes, now, accountId, owner);
        Sharing.saveTags(c, owner, accountId, body);
    }

    private Object importAccount(Connection c, String owner, JsonObject body) throws Exception {
        String platformName = checked(text(body, "platform_name", 1, 120).trim(), 1, 120);
        String name = optional(body, "name", 120);
        String login = checked(text(body, "login", 1, 160).trim(), 1, 160);
        Map<String, Object> platform = one(c, "SELECT id FROM am_platforms WHERE owner_id=? AND name=?", owner, platformName);
        if (platform != null && one(c, "SELECT id FROM am_accounts WHERE platform_id=? AND login=? AND name=?",
                platform.get("id"), login, name) != null) return object("status", "skipped");
        String platformId;
        if (platform == null) {
            platformId = UUID.randomUUID().toString();
            JsonObject platformBody = new JsonObject();
            platformBody.addProperty("url", optional(body, "platform_url", 1000));
            long now = System.currentTimeMillis();
            update(c, "INSERT INTO am_platforms(id,owner_id,name,url,created_at,updated_at) VALUES(?,?,?,?,?,?)",
                    platformId, owner, platformName, url(platformBody), now, now);
        } else platformId = (String) platform.get("id");
        JsonArray tagIds = new JsonArray();
        if (body.has("tag_names")) {
            JsonElement values = body.get("tag_names");
            if (!values.isJsonArray() || values.getAsJsonArray().size() > 20) throw new Problem(400, "invalid_tag_ids");
            for (JsonElement value : values.getAsJsonArray()) {
                JsonObject tagBody = new JsonObject(); tagBody.add("name", value); tagBody.addProperty("color", "blue");
                @SuppressWarnings("unchecked") Map<String, Object> tag = (Map<String, Object>) Sharing.tags(c, owner, "POST", tagBody);
                tagIds.add((String) tag.get("id"));
            }
        }
        JsonObject accountBody = body.deepCopy(); accountBody.addProperty("platform_id", platformId); accountBody.add("tag_ids", tagIds);
        String accountId = UUID.randomUUID().toString();
        saveAccount(c, owner, accountId, accountBody, true, null);
        return object("status", "created", "id", accountId);
    }

    private static String platformName(JsonObject body) { return checked(text(body, "name", 1, 120).trim(), 1, 120); }

    private static String url(JsonObject body) {
        String value = optional(body, "url", 1000).trim();
        if (!value.isEmpty()) {
            try {
                URI uri = new URI(value);
                if (!("https".equalsIgnoreCase(uri.getScheme()) || "http".equalsIgnoreCase(uri.getScheme()))
                        || uri.getHost() == null || uri.getUserInfo() != null) throw new Exception();
            } catch (Exception e) { throw new Problem(400, "invalid_url"); }
        }
        return value;
    }

    static String id(String value) {
        if (!value.matches("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"))
            throw new Problem(404, "not_found");
        return value;
    }

    static String text(JsonObject body, String name, int min, int max) {
        JsonElement value = body.get(name);
        if (value == null || !value.isJsonPrimitive() || !value.getAsJsonPrimitive().isString())
            throw new Problem(400, "invalid_field_" + name);
        return checked(value.getAsString(), min, max);
    }

    private static String optional(JsonObject body, String name, int max) {
        return body.has(name) ? text(body, name, 0, max) : "";
    }

    private static String checked(String value, int min, int max) {
        int length = value.codePointCount(0, value.length());
        if (length < min || length > max || value.indexOf('\0') >= 0) throw new Problem(400, "invalid_length");
        return value;
    }

    static long number(Map<String, Object> row, String field) { return ((Number) row.get(field)).longValue(); }

    static Map<String, Object> object(Object... pairs) {
        Map<String, Object> result = new LinkedHashMap<>();
        for (int i = 0; i < pairs.length; i += 2) result.put((String) pairs[i], pairs[i + 1]);
        return result;
    }
}

package accmgr;

import com.google.gson.*;
import java.sql.*;
import java.util.*;
import static accmgr.Database.*;
import static accmgr.Service.*;

/** Admin routes are separate from the owner's writable routes. */
final class AdminService {
    private final Service service;
    AdminService(Service service) { this.service = service; }

    Object register(Database db, JsonObject body) throws Exception {
        String code = text(body, "invite_code", 43, 43);
        String username = text(body, "username", 1, 100).trim();
        if (username.isEmpty()) throw new Problem(400, "invalid_field_username");
        String password = text(body, "password", 6, 256);
        try (Connection c = db.connections.open()) {
            c.setTransactionIsolation(Connection.TRANSACTION_READ_COMMITTED);
            c.setAutoCommit(false);
            try {
                Map<String, Object> invite = one(c,
                        "SELECT * FROM am_invitations WHERE code_hash=? FOR UPDATE", Crypto.digest(code));
                long now = System.currentTimeMillis();
                if (invite == null || number(invite, "revoked_at") != 0 || number(invite, "used_at") != 0
                        || number(invite, "expires_at") <= now) throw new Problem(400, "invalid_invitation");
                String userId = UUID.randomUUID().toString();
                update(c, "INSERT INTO am_users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)",
                        userId, username, Crypto.hashPassword(password), "user", now);
                update(c, "UPDATE am_invitations SET used_by=?,used_at=? WHERE id=?", userId, now, invite.get("id"));
                c.commit();
                return object("id", userId, "login_required", true);
            } catch (Exception e) {
                c.rollback();
                if (e instanceof SQLException && (((SQLException) e).getErrorCode() == 1062
                        || "23505".equals(((SQLException) e).getSQLState())))
                    throw new Problem(409, "username_taken");
                throw e;
            }
        }
    }

    Object execute(Connection c, String actor, String method, String path, JsonObject body,
                   Map<String, String> query) throws Exception {
        String[] parts = path.substring("/api/admin/".length()).split("/", -1);
        String action;
        String targetUser = null, target = null;
        if (parts.length == 1 && parts[0].equals("users")) {
            requireMethod(method, "GET");
            return rows(c, "SELECT u.id,u.username,u.role,u.active,u.must_change_password,u.created_at,"
                    + "(SELECT COUNT(*) FROM am_accounts a WHERE a.owner_id=u.id) AS account_count "
                    + "FROM am_users u ORDER BY u.created_at,u.id");
        }
        if (parts.length == 1 && parts[0].equals("audit-logs")) {
            requireMethod(method, "GET");
            return page(c, "am_audit_logs", "id,actor_id,target_user_id,target_id,action,outcome,created_at", query);
        }
        if (parts.length == 1 && parts[0].equals("invitations")) {
            if (method.equals("GET")) {
                List<Map<String, Object>> result = page(c, "am_invitations",
                        "id,created_by,created_at,expires_at,revoked_at,used_at,used_by", query);
                long now = System.currentTimeMillis();
                for (Map<String, Object> row : result) row.put("status", number(row, "used_at") != 0 ? "used"
                        : number(row, "revoked_at") != 0 ? "revoked" : number(row, "expires_at") <= now ? "expired" : "available");
                return result;
            }
            requireMethod(method, "POST");
            action = "invitation_create";
            target = UUID.randomUUID().toString();
        } else if (parts.length == 3 && parts[0].equals("invitations") && parts[2].equals("revoke")) {
            requireMethod(method, "POST");
            target = id(parts[1]); action = "invitation_revoke";
        } else if (parts.length >= 3 && parts[0].equals("users")) {
            targetUser = id(parts[1]);
            if (parts.length == 3 && parts[2].equals("status")) {
                requireMethod(method, "POST");
                JsonElement active = body.get("active");
                if (active == null || !active.isJsonPrimitive() || !active.getAsJsonPrimitive().isBoolean())
                    throw new Problem(400, "invalid_field_active");
                action = active.getAsBoolean() ? "user_enable" : "user_disable";
            } else if (parts.length == 3 && parts[2].equals("reset-password")) {
                requireMethod(method, "POST"); action = "password_reset";
            } else if ((parts.length == 3 || parts.length == 4)
                    && (parts[2].equals("platforms") || parts[2].equals("accounts"))) {
                requireMethod(method, "GET");
                if (parts.length == 4) target = id(parts[3]);
                action = parts[2].equals("platforms") ? "platform_read" : "account_read";
            } else if (parts.length == 5 && parts[2].equals("accounts") && parts[4].equals("password")) {
                requireMethod(method, "POST"); target = id(parts[3]);
                String purpose = text(body, "action", 4, 4);
                if (!purpose.equals("view") && !purpose.equals("copy")) throw new Problem(400, "invalid_field_action");
                action = "password_" + purpose;
            } else throw new Problem(404, "not_found");
        } else throw new Problem(404, "not_found");

        try {
            Object result = operate(c, actor, targetUser, target, action, body, query);
            audit(c, actor, targetUser, target, action, "success");
            return result; // Caller commits before a secret reaches the HTTP response.
        } catch (Exception e) {
            c.rollback();
            // Only fixed action names and validated IDs enter the log, never bodies or exception text.
            audit(c, actor, targetUser, target, action, "failure");
            c.commit();
            throw e;
        }
    }

    private Object operate(Connection c, String actor, String owner, String target, String action,
                           JsonObject body, Map<String, String> query) throws Exception {
        long now = System.currentTimeMillis();
        if (action.equals("invitation_create")) {
            String code = Crypto.token();
            long expires = now + SESSION_MILLIS;
            update(c, "INSERT INTO am_invitations(id,code_hash,created_by,created_at,expires_at) VALUES(?,?,?,?,?)",
                    target, Crypto.digest(code), actor, now, expires);
            return object("id", target, "invite_code", code, "expires_at", expires);
        }
        if (action.equals("invitation_revoke")) {
            Map<String, Object> invite = one(c, "SELECT * FROM am_invitations WHERE id=? FOR UPDATE", target);
            if (invite == null) throw new Problem(404, "not_found");
            if (number(invite, "used_at") != 0) throw new Problem(409, "invitation_used");
            if (number(invite, "revoked_at") == 0)
                update(c, "UPDATE am_invitations SET revoked_at=? WHERE id=?", now, target);
            return object("ok", true);
        }
        Map<String, Object> user = one(c, "SELECT id,username,role,active FROM am_users WHERE id=?", owner);
        if (user == null) throw new Problem(404, "not_found");
        if (action.equals("password_reset") || action.startsWith("user_")) {
            if (!"user".equals(user.get("role"))) throw new Problem(409, "admin_account_protected");
            // Same lock order as ordinary requests: user first, sessions second.
            one(c, "SELECT id FROM am_users WHERE id=? FOR UPDATE", owner);
            if (action.equals("password_reset")) {
                String temporary = Crypto.token().substring(0, 20);
                update(c, "UPDATE am_users SET password_hash=?,must_change_password=1,failed_logins=0,locked_until=0 WHERE id=?",
                        Crypto.hashPassword(temporary), owner);
                update(c, "DELETE FROM am_sessions WHERE user_id=?", owner);
                return object("temporary_password", temporary, "must_change_password", true);
            }
            boolean active = action.equals("user_enable");
            update(c, "UPDATE am_users SET active=? WHERE id=?", active ? 1 : 0, owner);
            if (!active) update(c, "DELETE FROM am_sessions WHERE user_id=?", owner);
            return object("ok", true);
        }
        Object data;
        if (action.equals("platform_read")) data = target == null
                ? service.platforms(c, owner, "GET", body) : service.platform(c, owner, target, "GET", body);
        else if (action.equals("account_read")) data = target == null
                ? service.accounts(c, owner, "GET", body, query) : service.account(c, owner, target, "GET", body, false);
        else data = service.account(c, owner, target, "POST", body, true);
        return object("user", user, "data", data);
    }

    private static void audit(Connection c, String actor, String owner, String target, String action, String outcome) throws Exception {
        update(c, "INSERT INTO am_audit_logs(id,actor_id,target_user_id,target_id,action,outcome,created_at) VALUES(?,?,?,?,?,?,?)",
                UUID.randomUUID().toString(), actor, owner, target, action, outcome, System.currentTimeMillis());
    }

    private static List<Map<String, Object>> page(Connection c, String table, String fields, Map<String, String> query) throws Exception {
        // Fixed table/column names supplied above; cursor values remain bound parameters.
        int limit = 100;
        try { if (query.containsKey("limit")) limit = Integer.parseInt(query.get("limit")); }
        catch (NumberFormatException e) { throw new Problem(400, "invalid_limit"); }
        if (limit < 1 || limit > 200) throw new Problem(400, "invalid_limit");
        String before = query.get("before_id");
        if (before == null) return rows(c, "SELECT " + fields + " FROM " + table + " ORDER BY created_at DESC,id DESC LIMIT " + limit);
        Map<String, Object> cursor = one(c, "SELECT created_at FROM " + table + " WHERE id=?", id(before));
        if (cursor == null) throw new Problem(400, "invalid_cursor");
        return rows(c, "SELECT " + fields + " FROM " + table + " WHERE created_at<? OR (created_at=? AND id<?) "
                + "ORDER BY created_at DESC,id DESC LIMIT " + limit, cursor.get("created_at"), cursor.get("created_at"), before);
    }

    private static void requireMethod(String actual, String expected) {
        if (!actual.equals(expected)) throw new Problem(405, "method_not_allowed");
    }
}

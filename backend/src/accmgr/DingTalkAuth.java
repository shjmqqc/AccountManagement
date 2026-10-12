package accmgr;

import com.google.gson.JsonObject;
import java.sql.*;
import java.util.*;
import static accmgr.Database.*;
import static accmgr.Service.*;

final class DingTalkAuth {
    private final Database db;
    private final Service service;
    final DingTalkClient client;
    DingTalkAuth(Database db, Service service, DingTalkClient client) { this.db = db; this.service = service; this.client = client; }

    Object login(JsonObject body, boolean bind) throws Exception {
        String dingUser = client.identify(text(body, "code", 1, 2048));
        try (Connection c = db.connections.open()) {
            c.setTransactionIsolation(Connection.TRANSACTION_READ_COMMITTED); c.setAutoCommit(false);
            try {
                Map<String, Object> user;
                if (bind) {
                    user = one(c, "SELECT * FROM am_users WHERE username=? FOR UPDATE", text(body, "username", 1, 100).trim());
                    service.verifyLoginPassword(c, user, text(body, "password", 1, 256));
                } else {
                    Map<String, Object> binding = one(c, "SELECT user_id FROM am_dingtalk_bindings WHERE corp_id=? AND ding_user_id=?", client.corpId, dingUser);
                    if (binding == null) { c.commit(); return object("bound", false); }
                    user = one(c, "SELECT * FROM am_users WHERE id=? FOR UPDATE", binding.get("user_id"));
                    if (user == null || number(user, "active") == 0 || number(user, "locked_until") > System.currentTimeMillis())
                        throw new Problem(401, "dingtalk_login_denied");
                    // Recheck after the owner lock: an unbind may have committed while waiting.
                    if (one(c, "SELECT user_id FROM am_dingtalk_bindings WHERE corp_id=? AND ding_user_id=? AND user_id=? FOR UPDATE",
                            client.corpId, dingUser, user.get("id")) == null) throw new Problem(401, "dingtalk_code_invalid");
                }
                if (number(user, "must_change_password") != 0) throw new Problem(403, "dingtalk_password_required");
                String owner = (String) user.get("id");
                if (bind) {
                    Map<String, Object> existing = one(c, "SELECT user_id,ding_user_id FROM am_dingtalk_bindings WHERE corp_id=? AND (ding_user_id=? OR user_id=?) FOR UPDATE",
                            client.corpId, dingUser, owner);
                    if (existing != null && !(owner.equals(existing.get("user_id")) && dingUser.equals(existing.get("ding_user_id"))))
                        throw new Problem(409, "dingtalk_already_bound");
                    if (existing == null) {
                        update(c, "INSERT INTO am_dingtalk_bindings(corp_id,ding_user_id,user_id,created_at) VALUES(?,?,?,?)", client.corpId, dingUser, owner, System.currentTimeMillis());
                        audit(c, owner, "dingtalk_bind");
                    }
                }
                Object result = service.newSession(c, user); c.commit(); return result;
            } catch (Exception e) { c.rollback(); throw e; }
        } catch (SQLException e) {
            if (e.getErrorCode() == 1062 || "23505".equals(e.getSQLState())) throw new Problem(409, "dingtalk_already_bound");
            if ("40001".equals(e.getSQLState())) throw new Problem(409, "concurrent_change_retry");
            throw e;
        }
    }

    Object binding(Connection c, Map<String, Object> user, String method, JsonObject body) throws Exception {
        String owner = (String) user.get("id");
        if (method.equals("GET")) return object("bound", one(c, "SELECT user_id FROM am_dingtalk_bindings WHERE corp_id=? AND user_id=?", client.corpId, owner) != null);
        if (!method.equals("DELETE")) throw new Problem(405, "method_not_allowed");
        service.verifyLoginPassword(c, user, text(body, "password", 1, 256));
        update(c, "DELETE FROM am_dingtalk_bindings WHERE corp_id=? AND user_id=?", client.corpId, owner);
        update(c, "DELETE FROM am_sessions WHERE user_id=?", owner);
        audit(c, owner, "dingtalk_unbind");
        return object("ok", true, "login_required", true);
    }

    private void audit(Connection c, String owner, String action) throws Exception {
        update(c, "INSERT INTO am_audit_logs(id,actor_id,target_user_id,target_id,action,outcome,created_at) VALUES(?,?,?,?,?,?,?)",
                UUID.randomUUID().toString(), owner, owner, owner, action, "success", System.currentTimeMillis());
    }
}

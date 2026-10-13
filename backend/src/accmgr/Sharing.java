package accmgr;

import com.google.gson.*;
import java.sql.*;
import java.util.*;
import static accmgr.Database.*;
import static accmgr.Service.*;

final class Sharing {
    private final Service service;
    Sharing(Service service) { this.service = service; }

    Object recipients(Connection c, String actor, String method, Map<String, String> query) throws Exception {
        requireMethod(method, "GET");
        String name = query.getOrDefault("q", "").trim();
        if (name.isEmpty() || name.codePointCount(0, name.length()) > 100) throw new Problem(400, "recipient_username_required");
        return rows(c, "SELECT u.id,u.username,COALESCE(b.ding_name,'') AS ding_name FROM am_users u "
                + "LEFT JOIN am_dingtalk_bindings b ON b.user_id=u.id AND b.corp_id=? "
                + "WHERE u.active=1 AND u.id<>? AND (u.username=? OR LOCATE(?,b.ding_name)>0) "
                + "ORDER BY u.username,u.id", service.dingCorpId(), actor, name, name);
    }

    Object grants(Connection c, String actor, String accountId, String recipientId, String method, JsonObject body) throws Exception {
        // Revoke and shared secret reads lock the same account before the grant, preventing stale authorization.
        Map<String, Object> account = one(c, "SELECT id FROM am_accounts WHERE id=? AND owner_id=? FOR UPDATE", accountId, actor);
        if (account == null) throw new Problem(404, "not_found");
        if (recipientId == null && method.equals("GET")) return rows(c,
                "SELECT u.id,u.username,u.active,s.created_at,COALESCE(b.ding_name,'') AS ding_name FROM am_account_shares s JOIN am_users u ON u.id=s.recipient_id "
                + "LEFT JOIN am_dingtalk_bindings b ON b.user_id=u.id AND b.corp_id=? "
                + "WHERE s.account_id=? ORDER BY s.created_at,u.id", service.dingCorpId(), accountId);
        String target = recipientId == null ? id(text(body, "recipient_id", 36, 36)) : recipientId;
        requireMethod(method, recipientId == null ? "POST" : "DELETE");
        String action = recipientId == null ? "share_grant" : "share_revoke";
        try {
            if (recipientId == null) {
                if (actor.equals(target) || one(c, "SELECT id FROM am_users WHERE id=? AND active=1", target) == null)
                    throw new Problem(400, "invalid_recipient");
                if (one(c, "SELECT account_id FROM am_account_shares WHERE account_id=? AND recipient_id=?", accountId, target) == null)
                    update(c, "INSERT INTO am_account_shares(account_id,recipient_id,created_at) VALUES(?,?,?)", accountId, target, System.currentTimeMillis());
            } else update(c, "DELETE FROM am_account_shares WHERE account_id=? AND recipient_id=?", accountId, target);
            audit(c, actor, target, accountId, action, "success");
            return object("ok", true);
        } catch (Exception e) {
            c.rollback(); audit(c, actor, target, accountId, action, "failure"); c.commit(); throw e;
        }
    }

    Object received(Connection c, String actor, String method, String suffix, JsonObject body, Map<String, String> query) throws Exception {
        if (suffix.isEmpty()) {
            requireMethod(method, "GET");
            List<Map<String, Object>> records = rows(c,
                    "SELECT a.id,a.platform_id,a.name,a.login,a.notes,a.created_at,a.updated_at,p.name AS platform_name,p.url AS platform_url,"
                    + "u.id AS owner_id,u.username AS owner_username FROM am_account_shares s "
                    + "JOIN am_accounts a ON a.id=s.account_id JOIN am_platforms p ON p.id=a.platform_id "
                    + "JOIN am_users u ON u.id=a.owner_id WHERE s.recipient_id=? AND u.active=1 ORDER BY a.updated_at DESC,a.id", actor);
            attachTags(c, records);
            filter(records, query);
            return records;
        }
        boolean reveal = suffix.endsWith("/password");
        String accountId = id(reveal ? suffix.substring(0, suffix.length() - 9) : suffix);
        requireMethod(method, reveal ? "POST" : "GET");
        String action = reveal ? text(body, "action", 4, 4) : "";
        if (reveal && !action.equals("view") && !action.equals("copy")) throw new Problem(400, "invalid_field_action");
        String owner = null;
        try {
            Map<String, Object> account = one(c, "SELECT owner_id FROM am_accounts WHERE id=? FOR UPDATE", accountId);
            if (account == null) throw new Problem(404, "not_found");
            owner = (String) account.get("owner_id");
            if (one(c, "SELECT account_id FROM am_account_shares WHERE account_id=? AND recipient_id=? FOR UPDATE", accountId, actor) == null
                    || one(c, "SELECT id FROM am_users WHERE id=? AND active=1", owner) == null) throw new Problem(404, "not_found");
            Object result = service.account(c, owner, accountId, method, body, reveal);
            if (reveal) audit(c, actor, owner, accountId, "shared_password_" + action, "success");
            return result;
        } catch (Exception e) {
            c.rollback();
            if (reveal) { audit(c, actor, owner, accountId, "shared_password_" + action, "failure"); c.commit(); }
            throw e;
        }
    }

    static Object tags(Connection c, String owner, String method, JsonObject body) throws Exception {
        if (method.equals("GET")) return rows(c, "SELECT t.id,t.name,t.color,(SELECT COUNT(*) FROM am_account_tags a WHERE a.tag_id=t.id) AS account_count FROM am_tags t WHERE t.owner_id=? ORDER BY t.name,t.id", owner);
        requireMethod(method, "POST");
        String name = text(body, "name", 1, 40).trim();
        if (name.isEmpty()) throw new Problem(400, "invalid_tag_name");
        String color = text(body, "color", 1, 10);
        if (!Arrays.asList("blue", "green", "orange", "red", "purple", "gray").contains(color)) throw new Problem(400, "invalid_tag_color");
        Map<String, Object> existing = one(c, "SELECT id,name,color FROM am_tags WHERE owner_id=? AND name=?", owner, name);
        if (existing != null) return existing; // Reuse the existing color, never mutate other accounts as a side effect.
        String tag = UUID.randomUUID().toString();
        update(c, "INSERT INTO am_tags(id,owner_id,name,color) VALUES(?,?,?,?)", tag, owner, name, color);
        return object("id", tag, "name", name, "color", color);
    }

    static Object tag(Connection c, String owner, String tag, String method, JsonObject body) throws Exception {
        if (one(c, "SELECT id FROM am_tags WHERE id=? AND owner_id=?", tag, owner) == null) throw new Problem(404, "not_found");
        if (method.equals("DELETE")) {
            update(c, "DELETE FROM am_tags WHERE id=? AND owner_id=?", tag, owner);
        } else if (method.equals("PUT")) {
            String name = text(body, "name", 1, 40).trim(), color = text(body, "color", 1, 10);
            if (name.isEmpty()) throw new Problem(400, "invalid_tag_name");
            if (!Arrays.asList("blue", "green", "orange", "red", "purple", "gray").contains(color)) throw new Problem(400, "invalid_tag_color");
            if (one(c, "SELECT id FROM am_tags WHERE owner_id=? AND name=? AND id<>?", owner, name, tag) != null)
                throw new Problem(409, "tag_name_taken");
            update(c, "UPDATE am_tags SET name=?,color=? WHERE id=? AND owner_id=?", name, color, tag, owner);
        } else throw new Problem(405, "method_not_allowed");
        return object("ok", true);
    }

    static void saveTags(Connection c, String owner, String account, JsonObject body) throws Exception {
        if (!body.has("tag_ids")) return; // Older clients preserve existing tags.
        JsonElement value = body.get("tag_ids");
        if (!value.isJsonArray() || value.getAsJsonArray().size() > 20) throw new Problem(400, "invalid_tag_ids");
        Set<String> ids = new LinkedHashSet<>();
        for (JsonElement element : value.getAsJsonArray()) {
            if (!element.isJsonPrimitive() || !element.getAsJsonPrimitive().isString()) throw new Problem(400, "invalid_tag_ids");
            String tag = id(element.getAsString());
            if (one(c, "SELECT id FROM am_tags WHERE id=? AND owner_id=?", tag, owner) == null) throw new Problem(400, "invalid_tag_ids");
            ids.add(tag);
        }
        update(c, "DELETE FROM am_account_tags WHERE account_id=?", account);
        for (String tag : ids) update(c, "INSERT INTO am_account_tags(account_id,tag_id) VALUES(?,?)", account, tag);
    }

    static void attachTags(Connection c, List<Map<String, Object>> records) throws Exception {
        if (records.isEmpty()) return;
        // Only load tags for already-authorized records, in batches to bound SQL parameter counts.
        Map<String, List<Map<String, Object>>> byAccount = new HashMap<>();
        for (Map<String, Object> record : records) {
            List<Map<String, Object>> tags = new ArrayList<>();
            record.put("tags", tags); byAccount.put((String) record.get("id"), tags);
        }
        List<String> ids = new ArrayList<>(byAccount.keySet());
        for (int offset = 0; offset < ids.size(); offset += 200) {
            List<String> batch = ids.subList(offset, Math.min(offset + 200, ids.size()));
            for (Map<String, Object> row : rows(c, "SELECT at.account_id,t.id,t.name,t.color FROM am_account_tags at "
                    + "JOIN am_tags t ON t.id=at.tag_id JOIN am_accounts a ON a.id=at.account_id AND a.owner_id=t.owner_id "
                    + "WHERE at.account_id IN (" + String.join(",", Collections.nCopies(batch.size(), "?")) + ") ORDER BY t.name,t.id", batch.toArray())) {
                String account = (String) row.remove("account_id"); byAccount.get(account).add(row);
            }
        }
    }

    @SuppressWarnings("unchecked")
    static void filter(List<Map<String, Object>> records, Map<String, String> query) {
        String search = query.getOrDefault("q", "").trim().toLowerCase(Locale.ROOT);
        if (search.codePointCount(0, search.length()) > 200 || search.indexOf('\0') >= 0) throw new Problem(400, "invalid_length");
        String platform = query.get("platform_id"), tag = query.get("tag_id");
        if (platform != null) id(platform);
        if (tag != null) id(tag);
        Iterator<Map<String, Object>> iterator = records.iterator();
        while (iterator.hasNext()) {
            Map<String, Object> row = iterator.next();
            boolean match = search.isEmpty();
            if (!search.isEmpty()) for (String field : new String[]{"platform_name", "name", "login", "notes"})
                match |= ((String) row.get(field)).toLowerCase(Locale.ROOT).contains(search);
            if (search.isEmpty() && platform != null) match &= platform.equals(row.get("platform_id"));
            if (tag != null) match &= ((List<Map<String, Object>>) row.get("tags")).stream().anyMatch(t -> tag.equals(t.get("id")));
            if (!match) iterator.remove();
        }
    }

    private static void audit(Connection c, String actor, String target, String account, String action, String outcome) throws Exception {
        update(c, "INSERT INTO am_audit_logs(id,actor_id,target_user_id,target_id,action,outcome,created_at) VALUES(?,?,?,?,?,?,?)",
                UUID.randomUUID().toString(), actor, target, account, action, outcome, System.currentTimeMillis());
    }
    private static void requireMethod(String actual, String expected) { if (!actual.equals(expected)) throw new Problem(405, "method_not_allowed"); }
}

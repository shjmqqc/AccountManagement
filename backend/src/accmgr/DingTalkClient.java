package accmgr;

import com.google.gson.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import javax.net.ssl.HttpsURLConnection;

/** Only the configured enterprise app can resolve identities; caller-supplied user IDs are never used. */
class DingTalkClient {
    final String corpId, clientId;
    private final String secret;
    private String accessToken;
    private long expiresAt;

    DingTalkClient(String corpId, String clientId, String secret) {
        if (!corpId.matches("[A-Za-z0-9_-]{1,128}") || clientId.trim().isEmpty() || secret.isEmpty())
            throw new IllegalArgumentException("Invalid DingTalk configuration");
        this.corpId = corpId; this.clientId = clientId; this.secret = secret;
    }

    String identify(String code) throws Exception {
        JsonObject response = request("https://oapi.dingtalk.com/topapi/v2/user/getuserinfo?access_token="
                + encode(accessToken()), Service.object("code", code));
        if (!response.has("errcode") || response.get("errcode").getAsInt() != 0)
            throw new Service.Problem(401, "dingtalk_code_invalid");
        JsonObject result = response.getAsJsonObject("result");
        if (result == null || !result.has("userid") || !result.get("userid").isJsonPrimitive())
            throw new Service.Problem(502, "dingtalk_unavailable");
        String id = result.get("userid").getAsString();
        if (!id.matches("[\\x21-\\x7E]{1,128}")) throw new Service.Problem(502, "dingtalk_unavailable");
        return id;
    }

    private synchronized String accessToken() throws Exception {
        if (accessToken != null && System.currentTimeMillis() < expiresAt) return accessToken;
        JsonObject response = request("https://oapi.dingtalk.com/gettoken?appkey=" + encode(clientId) + "&appsecret=" + encode(secret), null);
        if (!response.has("errcode") || response.get("errcode").getAsInt() != 0 || !response.has("access_token"))
            throw new Service.Problem(502, "dingtalk_unavailable");
        accessToken = response.get("access_token").getAsString();
        int seconds = response.has("expires_in") ? response.get("expires_in").getAsInt() : 7200;
        expiresAt = System.currentTimeMillis() + Math.max(0, Math.min(seconds, 7200) - 120) * 1000L;
        return accessToken;
    }

    private static String encode(String value) throws Exception { return URLEncoder.encode(value, "UTF-8"); }
    private static JsonObject request(String url, Object body) throws Exception {
        HttpsURLConnection connection = null;
        try {
            connection = (HttpsURLConnection) new URL(url).openConnection();
            connection.setConnectTimeout(5000); connection.setReadTimeout(5000);
            connection.setInstanceFollowRedirects(false);
            if (body != null) {
                connection.setRequestMethod("POST"); connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                try (OutputStream stream = connection.getOutputStream()) { stream.write(new Gson().toJson(body).getBytes(StandardCharsets.UTF_8)); }
            }
            if (connection.getResponseCode() != 200) throw new IOException("DingTalk request failed");
            try (InputStream input = connection.getInputStream(); ByteArrayOutputStream bytes = new ByteArrayOutputStream()) {
                byte[] buffer = new byte[2048]; int size;
                while ((size = input.read(buffer)) != -1) {
                    if (bytes.size() + size > 65536) throw new IOException("Response too large");
                    bytes.write(buffer, 0, size);
                }
                return JsonParser.parseString(new String(bytes.toByteArray(), StandardCharsets.UTF_8)).getAsJsonObject();
            }
        } catch (Exception error) {
            // No URLs, credentials, authorization codes or upstream error bodies in client responses/logs.
            throw new Service.Problem(502, "dingtalk_unavailable");
        } finally { if (connection != null) connection.disconnect(); }
    }
}

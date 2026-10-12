package accmgr;

import com.google.gson.*;
import com.sun.net.httpserver.*;
import java.io.*;
import java.net.*;
import java.nio.ByteBuffer;
import java.nio.charset.*;
import java.util.*;
import java.util.concurrent.*;

final class ApiServer implements AutoCloseable {
    private static final Gson JSON = new GsonBuilder().setStrictness(Strictness.STRICT).create();
    private final HttpServer server;
    private final ThreadPoolExecutor executor;
    private final Service service;
    private final String browserOrigin;

    ApiServer(Service service, int port) throws IOException {
        this(service, "127.0.0.1", port);
    }

    ApiServer(Service service, String host, int port) throws IOException {
        this(service, host, port, "");
    }

    ApiServer(Service service, String host, int port, String browserOrigin) throws IOException {
        this.service = service;
        this.browserOrigin = browserOrigin.trim();
        if (!this.browserOrigin.isEmpty()) {
            URI origin = URI.create(this.browserOrigin);
            if (!("https".equals(origin.getScheme()) || "http".equals(origin.getScheme()))
                    || origin.getHost() == null || origin.getUserInfo() != null || !origin.getRawPath().isEmpty()
                    || origin.getRawQuery() != null || origin.getRawFragment() != null)
                throw new IllegalArgumentException("Invalid browser origin");
        }
        if (host == null || host.trim().isEmpty()) throw new IllegalArgumentException("Empty HTTP host");
        server = HttpServer.create(new InetSocketAddress(host.trim(), port), 32);
        executor = new ThreadPoolExecutor(4, 4, 0, TimeUnit.SECONDS,
                new ArrayBlockingQueue<Runnable>(32), new ThreadPoolExecutor.CallerRunsPolicy());
        server.setExecutor(executor);
        server.createContext("/", this::handle);
    }

    void start() { server.start(); }
    int port() { return server.getAddress().getPort(); }
    InetSocketAddress address() { return server.getAddress(); }
    public void close() { server.stop(0); executor.shutdownNow(); }

    private void handle(HttpExchange exchange) throws IOException {
        int status = 200;
        Object result;
        try {
            String path = exchange.getRequestURI().getPath();
            if (path.equals("/health") && exchange.getRequestMethod().equals("GET")) {
                result = Service.object("status", "ok", "service", "account-management-core");
            } else {
                String origin = exchange.getRequestHeaders().getFirst("Origin");
                if (origin != null && (browserOrigin.isEmpty() || !browserOrigin.equals(origin)))
                    throw new Service.Problem(403, "browser_origin_not_allowed");
                String authorization = exchange.getRequestHeaders().getFirst("Authorization");
                String token = authorization != null && authorization.startsWith("Bearer ") ? authorization.substring(7) : null;
                JsonObject body = readBody(exchange);
                result = service.execute(exchange.getRequestMethod(), path, token, body, query(exchange.getRequestURI()));
            }
        } catch (Service.Problem e) {
            status = e.status;
            result = Service.object("error", e.getMessage());
        } catch (JsonParseException | CharacterCodingException | IllegalArgumentException e) {
            status = 400;
            result = Service.object("error", "invalid_request");
        } catch (Exception e) {
            // Never log request bodies, authorization headers or driver exception messages.
            System.err.println("Request failed: " + e.getClass().getSimpleName());
            status = 500;
            result = Service.object("error", "internal_error");
        }
        byte[] bytes = JSON.toJson(result).getBytes(StandardCharsets.UTF_8);
        exchange.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
        exchange.getResponseHeaders().set("Cache-Control", "no-store");
        exchange.getResponseHeaders().set("X-Content-Type-Options", "nosniff");
        try {
            exchange.sendResponseHeaders(status, bytes.length);
            exchange.getResponseBody().write(bytes);
        } finally { exchange.close(); }
    }

    private static JsonObject readBody(HttpExchange exchange) throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        byte[] buffer = new byte[2048];
        int count;
        while ((count = exchange.getRequestBody().read(buffer)) != -1) {
            if (bytes.size() + count > 16384) throw new Service.Problem(413, "body_too_large");
            bytes.write(buffer, 0, count);
        }
        if (bytes.size() == 0) return new JsonObject();
        String contentType = exchange.getRequestHeaders().getFirst("Content-Type");
        if (contentType == null || !contentType.split(";", 2)[0].trim().equalsIgnoreCase("application/json"))
            throw new Service.Problem(415, "json_required");
        String text = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes.toByteArray())).toString();
        JsonElement parsed = JSON.fromJson(text, JsonElement.class);
        if (parsed == null || !parsed.isJsonObject()) throw new Service.Problem(400, "json_object_required");
        return parsed.getAsJsonObject();
    }

    private static Map<String, String> query(URI uri) throws UnsupportedEncodingException {
        Map<String, String> result = new HashMap<>();
        String query = uri.getRawQuery();
        if (query == null) return result;
        if (query.length() > 4096) throw new Service.Problem(400, "query_too_large");
        for (String item : query.split("&")) {
            String[] pair = item.split("=", 2);
            String name = URLDecoder.decode(pair[0], "UTF-8");
            if (result.containsKey(name)) throw new Service.Problem(400, "duplicate_query_parameter");
            result.put(name, pair.length == 2 ? URLDecoder.decode(pair[1], "UTF-8") : "");
        }
        return result;
    }
}

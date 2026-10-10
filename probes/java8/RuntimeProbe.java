import com.sun.net.httpserver.HttpServer;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.ExecutorService;

/** Compatibility probe only; not the account-management API. */
public final class RuntimeProbe {
    public static void main(String[] args) throws Exception {
        int port = args.length == 0 ? 18081 : Integer.parseInt(args[0]);
        if (port < 1 || port > 65535) {
            throw new IllegalArgumentException("Port must be between 1 and 65535");
        }
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", port), 8);
        ExecutorService executor = Executors.newFixedThreadPool(2);
        server.setExecutor(executor);
        server.createContext("/", exchange -> {
            int status;
            String body;
            if (!"/health".equals(exchange.getRequestURI().getPath())) {
                status = 404;
                body = "{\"error\":\"not_found\"}";
            } else if (!"GET".equals(exchange.getRequestMethod())) {
                status = 405;
                body = "{\"error\":\"method_not_allowed\"}";
                exchange.getResponseHeaders().set("Allow", "GET");
            } else {
                status = 200;
                body = "{\"status\":\"ok\",\"service\":\"java8-runtime-probe\"}";
            }
            byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
            exchange.getResponseHeaders().set("Cache-Control", "no-store");
            try {
                exchange.sendResponseHeaders(status, bytes.length);
                exchange.getResponseBody().write(bytes);
            } finally {
                exchange.close();
            }
        });
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            server.stop(0);
            executor.shutdownNow();
        }));
        server.start();
        System.out.println("Probe listening on http://127.0.0.1:" + port + "/health");
    }
}

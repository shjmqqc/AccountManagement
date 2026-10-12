package accmgr;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.nio.file.attribute.PosixFilePermissions;
import java.sql.Connection;
import java.util.*;

public final class Main {
    public static void main(String[] args) {
        try { run(args); }
        catch (Exception e) {
            // Config may contain passwords. Print only a safe error category.
            System.err.println("Startup failed: " + e.getClass().getSimpleName()
                    + (e instanceof Service.Problem ? " (" + e.getMessage() + ")" : "")
                    + ". Check configuration, key file, database grants and schema.");
            System.exit(1);
        }
    }

    private static void run(String[] args) throws Exception {
        if (args.length != 2 || !(args[0].equals("keygen") || args[0].equals("init-admin") || args[0].equals("serve"))) {
            System.err.println("Usage: Main keygen <new-key-file> | init-admin <config-file> | serve <config-file>");
            System.exit(2);
        }
        if (args[0].equals("keygen")) {
            Path path = Paths.get(args[1]);
            // CREATE_NEW semantics prevent accidental replacement of an existing vault key.
            if (FileSystems.getDefault().supportedFileAttributeViews().contains("posix"))
                Files.createFile(path, PosixFilePermissions.asFileAttribute(PosixFilePermissions.fromString("rw-------")));
            else Files.createFile(path);
            Files.write(path, Base64.getEncoder().encode(Crypto.random(32)), StandardOpenOption.WRITE);
            System.out.println("Key file created. Back it up separately; never replace it after storing passwords.");
            return;
        }
        Properties config = new Properties();
        try (Reader reader = Files.newBufferedReader(Paths.get(args[1]), StandardCharsets.UTF_8)) { config.load(reader); }
        Database db = Database.mysql(config);
        byte[] key = Base64.getDecoder().decode(new String(Files.readAllBytes(
                Paths.get(Database.required(config, "key.file"))), StandardCharsets.US_ASCII).trim());
        Crypto crypto = new Crypto(key);
        Arrays.fill(key, (byte) 0);
        Service service = new Service(db, crypto);
        if (args[0].equals("init-admin")) {
            Console console = System.console();
            if (console == null) throw new IllegalStateException("Interactive terminal required");
            String username = console.readLine("Administrator username: ");
            char[] first = console.readPassword("Administrator password (at least 6 characters): ");
            char[] second = console.readPassword("Repeat password: ");
            try {
                if (username == null || first == null || second == null || !Arrays.equals(first, second))
                    throw new Service.Problem(400, "password_confirmation_failed");
                service.bootstrap(username, new String(first));
            } finally {
                if (first != null) Arrays.fill(first, '\0');
                if (second != null) Arrays.fill(second, '\0');
            }
            System.out.println("Administrator initialized.");
            return;
        }
        // Fail before binding if schema/key configuration is unusable.
        try (Connection c = db.connections.open()) {
            Database.rows(c, "SELECT token_hash FROM am_sessions WHERE 1=0");
            Database.rows(c, "SELECT code_hash FROM am_invitations WHERE 1=0");
            Database.rows(c, "SELECT action FROM am_audit_logs WHERE 1=0");
            Database.rows(c, "SELECT id FROM am_tags WHERE 1=0");
            Database.rows(c, "SELECT tag_id FROM am_account_tags WHERE 1=0");
            Database.rows(c, "SELECT recipient_id FROM am_account_shares WHERE 1=0");
            Map<String, Object> sample = Database.one(c, "SELECT id,owner_id,password_cipher FROM am_accounts LIMIT 1");
            if (sample != null) crypto.decrypt((String) sample.get("password_cipher"), sample.get("owner_id") + ":" + sample.get("id"));
        }
        int port = Integer.parseInt(config.getProperty("http.port", "18082"));
        if (port < 1 || port > 65535) throw new IllegalArgumentException("Invalid HTTP port");
        String host = config.getProperty("http.host", "127.0.0.1").trim();
        ApiServer server = new ApiServer(service, host, port, config.getProperty("http.browserOrigin", ""));
        Runtime.getRuntime().addShutdownHook(new Thread(server::close));
        server.start();
        System.out.println("Core API listening on " + server.address() + " (HTTP; HTTPS can be terminated by Nginx)");
    }
}

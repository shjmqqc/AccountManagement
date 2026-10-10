package accmgr;

import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.sql.*;
import java.util.UUID;

/** Local-only desktop integration fixture: real service code, disposable H2 data. */
public final class Fixture {
    public static void main(String[] args) throws Exception {
        Class.forName("org.h2.Driver");
        Database db = new Database(() -> DriverManager.getConnection("jdbc:h2:mem:desktop;MODE=MySQL;DATABASE_TO_LOWER=TRUE;DB_CLOSE_DELAY=-1", "sa", ""));
        String ddl = new String(Files.readAllBytes(Paths.get("../database/002-core-schema.sql")), StandardCharsets.UTF_8)
            .replaceAll("(?m)^--.*$", "").replace("USE `account_management`;", "")
            .replaceAll(" CHARACTER SET [a-zA-Z0-9_]+", "").replaceAll(" COLLATE[= ]+[a-zA-Z0-9_]+", "")
            .replaceAll(" ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", "");
        try (Connection c = db.connections.open(); Statement s = c.createStatement()) {
            for (String sql : ddl.split(";")) if (!sql.trim().isEmpty()) s.execute(sql);
        }
        Service service = new Service(db, new Crypto(Crypto.random(32)));
        service.bootstrap("desktop-test", "test123456");
        try (Connection c = db.connections.open()) {
            Database.update(c, "INSERT INTO am_users(id,username,password_hash,role,created_at) VALUES(?,?,?,?,?)",
                UUID.randomUUID().toString(), "other-test", Crypto.hashPassword("test123456"), "user", System.currentTimeMillis());
        }
        ApiServer server = new ApiServer(service, Integer.parseInt(args[0]));
        Runtime.getRuntime().addShutdownHook(new Thread(server::close));
        server.start();
        System.out.println("READY " + server.port());
    }
}

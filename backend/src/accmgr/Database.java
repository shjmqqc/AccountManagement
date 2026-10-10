package accmgr;

import java.sql.*;
import java.util.*;

final class Database {
    interface Connections { Connection open() throws SQLException; }
    final Connections connections;

    Database(Connections connections) { this.connections = connections; }

    static Database mysql(Properties config) throws Exception {
        Class.forName("com.mysql.jdbc.Driver");
        int port = Integer.parseInt(config.getProperty("db.port", "3306"));
        if (port < 1 || port > 65535) throw new IllegalArgumentException("Invalid database port");
        Properties options = new Properties();
        options.setProperty("user", required(config, "db.user"));
        options.setProperty("password", required(config, "db.password"));
        options.setProperty("useUnicode", "true");
        options.setProperty("characterEncoding", "UTF-8");
        options.setProperty("connectionCollation", "utf8mb4_unicode_ci");
        options.setProperty("useSSL", "false"); // Loopback only.
        options.setProperty("connectTimeout", "5000");
        options.setProperty("socketTimeout", "10000");
        options.setProperty("jdbcCompliantTruncation", "true");
        String url = "jdbc:mysql://127.0.0.1:" + port + "/account_management";
        return new Database(() -> DriverManager.getConnection(url, options));
    }

    static String required(Properties config, String name) {
        String value = config.getProperty(name);
        if (value == null || value.isEmpty() || value.startsWith("REPLACE_"))
            throw new IllegalArgumentException("Configure " + name);
        return value;
    }

    static PreparedStatement statement(Connection c, String sql, Object... values) throws SQLException {
        PreparedStatement statement = c.prepareStatement(sql);
        for (int i = 0; i < values.length; i++) statement.setObject(i + 1, values[i]);
        return statement;
    }

    static int update(Connection c, String sql, Object... values) throws SQLException {
        try (PreparedStatement s = statement(c, sql, values)) { return s.executeUpdate(); }
    }

    static List<Map<String, Object>> rows(Connection c, String sql, Object... values) throws SQLException {
        try (PreparedStatement s = statement(c, sql, values); ResultSet r = s.executeQuery()) {
            List<Map<String, Object>> rows = new ArrayList<>();
            while (r.next()) {
                Map<String, Object> row = new LinkedHashMap<>();
                for (int i = 1; i <= r.getMetaData().getColumnCount(); i++)
                    row.put(r.getMetaData().getColumnLabel(i).toLowerCase(Locale.ROOT), r.getObject(i));
                rows.add(row);
            }
            return rows;
        }
    }

    static Map<String, Object> one(Connection c, String sql, Object... values) throws SQLException {
        List<Map<String, Object>> results = rows(c, sql, values);
        return results.isEmpty() ? null : results.get(0);
    }
}

import java.io.Console;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.Arrays;
import java.util.Properties;

/** Read-only, loopback-only database compatibility check. */
public final class DatabaseProbe {
    public static void main(String[] args) {
        try {
            run(args);
        } catch (SQLException e) {
            // Do not print driver messages: they can contain connection details.
            System.err.println("FAIL database check: SQLState=" + e.getSQLState()
                    + ", code=" + e.getErrorCode());
            System.exit(1);
        } catch (Exception e) {
            System.err.println("FAIL: " + e.getMessage());
            System.exit(1);
        }
    }

    private static void run(String[] args) throws Exception {
        if (args.length > 1) {
            throw new IllegalArgumentException("Usage: DatabaseProbe [local-port]");
        }
        int port = args.length == 0 ? 3306 : Integer.parseInt(args[0]);
        if (port < 1 || port > 65535) {
            throw new IllegalArgumentException("Port must be between 1 and 65535");
        }
        Class.forName("com.mysql.jdbc.Driver");
        Console console = System.console();
        if (console == null) {
            throw new IllegalStateException("Run from an interactive terminal for hidden password input");
        }
        char[] password = console.readPassword("Password for am_probe: ");
        if (password == null || password.length == 0) {
            throw new IllegalArgumentException("Password is required");
        }
        Properties properties = new Properties();
        properties.setProperty("user", "am_probe");
        properties.setProperty("password", new String(password));
        Arrays.fill(password, '\0');
        properties.setProperty("useUnicode", "true");
        properties.setProperty("characterEncoding", "UTF-8");
        properties.setProperty("connectionCollation", "utf8mb4_unicode_ci");
        // This probe connects only to loopback on the database server.
        properties.setProperty("useSSL", "false");
        properties.setProperty("connectTimeout", "5000");
        properties.setProperty("socketTimeout", "5000");
        String url = "jdbc:mysql://127.0.0.1:" + port + "/account_management";
        try (Connection connection = DriverManager.getConnection(url, properties)) {
            properties.remove("password");
            try (Statement statement = connection.createStatement();
                 ResultSet result = statement.executeQuery("SELECT VERSION(), DATABASE(), "
                         + "@@character_set_database, @@character_set_client, "
                         + "@@character_set_connection, @@character_set_results")) {
                if (!result.next()) {
                    throw new IllegalStateException("Missing database metadata");
                }
                System.out.println("server_version=" + result.getString(1));
                if (!"account_management".equals(result.getString(2))) {
                    throw new IllegalStateException("Unexpected database");
                }
                for (int column = 3; column <= 5; column++) {
                    if (!"utf8mb4".equals(result.getString(column))) {
                        throw new IllegalStateException("Expected utf8mb4 at metadata column " + column);
                    }
                }
                String resultsCharset = result.getString(6);
                System.out.println("character_set_results=" + resultsCharset);
                validateResultsCharset(resultsCharset);
            }
            String sample = "中文帐号验证\uD83D\uDD10";
            try (PreparedStatement statement = connection.prepareStatement("SELECT ?")) {
                statement.setString(1, sample);
                try (ResultSet result = statement.executeQuery()) {
                    if (!result.next() || !sample.equals(result.getString(1))) {
                        throw new IllegalStateException("Unicode round-trip failed");
                    }
                }
            }
            System.out.println("PASS connection, database charset and Chinese/emoji round-trip");
            System.out.println("No tables or business data were written. Storage round-trip remains untested.");
        } finally {
            properties.remove("password");
        }
    }

    static void validateResultsCharset(String charset) {
        // Connector/J uses SQL NULL to preserve each column's original encoding.
        // Actual decoding is checked by the Chinese/emoji round-trip below.
        if (charset != null && !"utf8mb4".equalsIgnoreCase(charset)) {
            throw new IllegalStateException("Unexpected character_set_results: " + charset);
        }
    }
}

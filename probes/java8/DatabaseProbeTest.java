/** Regression check; no database or credentials required. */
public final class DatabaseProbeTest {
    public static void main(String[] args) {
        DatabaseProbe.validateResultsCharset(null);
        DatabaseProbe.validateResultsCharset("utf8mb4");
        for (String charset : new String[] {"latin1", "utf8", ""}) {
            try {
                DatabaseProbe.validateResultsCharset(charset);
                throw new AssertionError("Accepted incompatible charset: " + charset);
            } catch (IllegalStateException expected) {
                // Non-Unicode or three-byte conversion must still fail.
            }
        }
        System.out.println("PASS results charset: SQL NULL and utf8mb4 accepted; incompatible conversions rejected");
    }
}

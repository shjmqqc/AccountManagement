package accmgr;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.Arrays;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;

final class Crypto {
    private static final SecureRandom RANDOM = new SecureRandom();
    private static final int ITERATIONS = 600000;
    private final SecretKeySpec key;

    Crypto(byte[] keyBytes) {
        if (keyBytes.length != 32) throw new IllegalArgumentException("Key must contain 32 bytes");
        key = new SecretKeySpec(keyBytes, "AES");
    }

    static byte[] random(int length) {
        byte[] bytes = new byte[length];
        RANDOM.nextBytes(bytes);
        return bytes;
    }

    static String token() {
        return Base64.getUrlEncoder().withoutPadding().encodeToString(random(32));
    }

    static String digest(String value) throws Exception {
        byte[] bytes = MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8));
        StringBuilder result = new StringBuilder();
        for (byte b : bytes) result.append(String.format("%02x", b & 255));
        return result.toString();
    }

    static String hashPassword(String password) throws Exception {
        byte[] salt = random(16);
        return "pbkdf2-sha256$" + ITERATIONS + "$" + Base64.getEncoder().encodeToString(salt)
                + "$" + Base64.getEncoder().encodeToString(derive(password, salt, ITERATIONS));
    }

    static boolean verifyPassword(String password, String encoded) throws Exception {
        String[] parts = encoded.split("\\$");
        if (parts.length != 4 || !parts[0].equals("pbkdf2-sha256")) return false;
        int rounds = Integer.parseInt(parts[1]);
        if (rounds != ITERATIONS) return false;
        return MessageDigest.isEqual(Base64.getDecoder().decode(parts[3]),
                derive(password, Base64.getDecoder().decode(parts[2]), rounds));
    }

    private static byte[] derive(String password, byte[] salt, int rounds) throws Exception {
        char[] chars = password.toCharArray();
        PBEKeySpec spec = new PBEKeySpec(chars, salt, rounds, 256);
        Arrays.fill(chars, '\0');
        try {
            return SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).getEncoded();
        } finally {
            spec.clearPassword();
        }
    }

    String encrypt(String plaintext, String context) throws Exception {
        byte[] nonce = random(12);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key, new GCMParameterSpec(128, nonce));
        cipher.updateAAD(context.getBytes(StandardCharsets.UTF_8));
        byte[] encrypted = cipher.doFinal(plaintext.getBytes(StandardCharsets.UTF_8));
        return "v1." + Base64.getEncoder().encodeToString(nonce) + "."
                + Base64.getEncoder().encodeToString(encrypted);
    }

    String decrypt(String encoded, String context) throws Exception {
        String[] parts = encoded.split("\\.");
        if (parts.length != 3 || !parts[0].equals("v1")) throw new IllegalArgumentException("Invalid ciphertext");
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(128, Base64.getDecoder().decode(parts[1])));
        cipher.updateAAD(context.getBytes(StandardCharsets.UTF_8));
        return new String(cipher.doFinal(Base64.getDecoder().decode(parts[2])), StandardCharsets.UTF_8);
    }
}

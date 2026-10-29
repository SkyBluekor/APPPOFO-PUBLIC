package com.apppofo.cloud;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.security.KeyPairGenerator;
import java.security.KeyStore;
import java.security.PrivateKey;
import java.security.PublicKey;
import java.security.Signature;
import java.security.interfaces.ECPublicKey;
import java.security.spec.ECGenParameterSpec;

/**
 * Device-bound ECDSA P-256 identity for S5-04B.
 * Private keys never leave AndroidKeyStore. Never return or log a private key.
 */
public final class AppPofoDeviceKeyStore {
    private static final String ALIAS = "AppPofo.cloud.auth.p256.v1";
    private AppPofoDeviceKeyStore() {}

    private static KeyStore store() throws Exception {
        KeyStore ks = KeyStore.getInstance("AndroidKeyStore");
        ks.load(null);
        return ks;
    }

    private static synchronized PublicKey ensurePublicKey(String alias, boolean allowCreate) throws Exception {
        KeyStore ks = store();
        java.security.cert.Certificate cert = ks.getCertificate(alias);
        if (cert != null) return cert.getPublicKey();
        if (!allowCreate) throw new IllegalStateException("Remembered device key unavailable");
        KeyPairGenerator generator = KeyPairGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore");
        KeyGenParameterSpec spec = new KeyGenParameterSpec.Builder(
            alias, KeyProperties.PURPOSE_SIGN)
            .setAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
            .setDigests(KeyProperties.DIGEST_SHA256)
            .setUserAuthenticationRequired(false)
            .build();
        generator.initialize(spec);
        return generator.generateKeyPair().getPublic();
    }

    private static byte[] fixed32(BigInteger value) {
        byte[] variable = value.toByteArray();
        if (variable.length > 33 || (variable.length == 33 && variable[0] != 0))
            throw new IllegalArgumentException("Unexpected P-256 coordinate size");
        byte[] fixed = new byte[32];
        if (variable.length == 33) {
            System.arraycopy(variable, 1, fixed, 0, 32);
        } else {
            if (variable.length > 32)
                throw new IllegalArgumentException("Invalid coordinate");
            System.arraycopy(variable, 0, fixed, 32 - variable.length, variable.length);
        }
        return fixed;
    }

    private static String encode(byte[] value) {
        return Base64.encodeToString(value,
            Base64.URL_SAFE | Base64.NO_PADDING | Base64.NO_WRAP);
    }

    public static String getPublicKeyJwkJson() throws Exception {
        return getPublicKeyJwkJsonForScope("", true);
    }

    private static String aliasFor(String scope) {
        if (scope == null || (!scope.isEmpty() && !scope.matches("[A-F0-9]{24}")))
            throw new IllegalArgumentException("Invalid device key scope");
        return scope.isEmpty() ? ALIAS : ALIAS + "." + scope;
    }

    public static String getPublicKeyJwkJsonForScope(String scope, boolean allowCreate) throws Exception {
        ECPublicKey key = (ECPublicKey) ensurePublicKey(aliasFor(scope), allowCreate);
        String x = encode(fixed32(key.getW().getAffineX()));
        String y = encode(fixed32(key.getW().getAffineY()));
        return "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"" + x +
            "\",\"y\":\"" + y + "\"}";
    }

    // Produces strict DER SHA256withECDSA; Worker converts it for WebCrypto.
    public static String signMessage(String message) throws Exception {
        ensurePublicKey(ALIAS, true);
        return signMessageForScope("", message);
    }

    public static String signMessageForScope(String scope, String message) throws Exception {
        String alias = aliasFor(scope);
        ensurePublicKey(alias, false);
        PrivateKey key = (PrivateKey) store().getKey(alias, null);
        if (key == null) throw new IllegalStateException("AndroidKeyStore key unavailable");
        Signature signature = Signature.getInstance("SHA256withECDSA");
        signature.initSign(key);
        signature.update(message.getBytes(StandardCharsets.UTF_8));
        return encode(signature.sign());
    }
}

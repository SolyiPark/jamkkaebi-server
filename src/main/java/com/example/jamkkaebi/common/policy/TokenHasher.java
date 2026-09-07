package com.example.jamkkaebi.common.policy;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Base64;

/**
 * 토큰·verifier 를 저장·비교용 해시로 바꾼다.
 *
 * <p>Refresh Token 과 verifier 는 <b>원문을 저장하지 않는다</b> — DB 가 유출돼도 그대로 쓸 수 없게
 * 하기 위해서다. 양쪽이 같은 방식으로 해시해야 대조가 되므로 한곳에 모아 둔다.
 */
public final class TokenHasher {

    private TokenHasher() {
    }

    public static String sha256(String value) {
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            return Base64.getEncoder()
                    .encodeToString(digest.digest(value.getBytes(StandardCharsets.UTF_8)));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 알고리즘을 사용할 수 없습니다.", e);
        }
    }

    /**
     * 타이밍 공격에 흔들리지 않게 두 해시를 비교한다.
     *
     * <p>{@code equals} 는 다른 글자를 만나는 즉시 멈춰, 응답 시간 차이로 앞자리를 한 글자씩 맞춰
     * 나갈 여지를 준다. 인계 코드는 60초짜리라 실익이 크지 않지만, 비교 방식을 바꾸는 비용이
     * 사실상 없으므로 안전한 쪽을 쓴다.
     */
    public static boolean matches(String rawValue, String expectedHash) {
        if (rawValue == null || expectedHash == null) {
            return false;
        }
        return MessageDigest.isEqual(
                sha256(rawValue).getBytes(StandardCharsets.UTF_8),
                expectedHash.getBytes(StandardCharsets.UTF_8));
    }
}

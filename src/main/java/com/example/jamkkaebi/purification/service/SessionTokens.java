package com.example.jamkkaebi.purification.service;

import org.springframework.stereotype.Component;

import java.security.SecureRandom;
import java.util.Base64;

/**
 * 세션 토큰과 보드 시드를 만든다.
 */
@Component
public class SessionTokens {

    // 16바이트 = 22자 URL-safe 문자열
    private static final int TOKEN_BYTES = 16;

    private final SecureRandom random = new SecureRandom();
    private final Base64.Encoder encoder = Base64.getUrlEncoder().withoutPadding();

    public String newToken() {
        byte[] bytes = new byte[TOKEN_BYTES];
        random.nextBytes(bytes);
        return encoder.encodeToString(bytes);
    }

    public long newBoardSeed() {
        return random.nextLong() & Long.MAX_VALUE;
    }
}

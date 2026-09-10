package com.example.jamkkaebi.auth.domain;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;

import java.util.Locale;

/**
 * 지원하는 소셜 로그인 제공자. 게스트 계정은 없다.
 */
public enum AuthProvider {
    GOOGLE, // 구글
    KAKAO;  // 카카오

    /**
     * Spring Security 의 registrationId(소문자 {@code google}/{@code kakao})를 열거형으로 바꾼다.
     *
     * <p>설정에 없는 값이 오면 A006 으로 막는다 — 열거형에 없는 registration 이 등록되면 그 사용자는
     * provider 가 비어 있는 채로 가입될 수 있어서, 조용히 넘기지 않는다.
     */
    public static AuthProvider fromRegistrationId(String registrationId) {
        try {
            return valueOf(registrationId.toUpperCase(Locale.ROOT));
        } catch (IllegalArgumentException | NullPointerException e) {
            throw new BusinessException(ErrorCode.UNSUPPORTED_AUTH_PROVIDER);
        }
    }
}

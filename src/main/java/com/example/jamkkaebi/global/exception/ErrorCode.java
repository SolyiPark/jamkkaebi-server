package com.example.jamkkaebi.global.exception;

import lombok.Getter;
import lombok.RequiredArgsConstructor;
import org.springframework.http.HttpStatus;

/**
 * 서비스 전역 오류 코드. HTTP 상태 + 코드 + 사용자 메시지를 한곳에 모은다.
 *
 * <p>게임 클라이언트는 코드로 분기한다 — 특히 {@code A004}(만료)와 {@code A003}/{@code A005}
 * (위조·불일치)를 나눠 두는 이유는 전자는 조용히 재발급, 후자는 재로그인으로 처리가 갈리기 때문이다.
 */
@Getter
@RequiredArgsConstructor
public enum ErrorCode {

    // Common
    INVALID_INPUT(HttpStatus.BAD_REQUEST, "C001", "잘못된 입력입니다."),
    CONFLICT(HttpStatus.CONFLICT, "C003", "요청이 현재 상태와 충돌합니다."),
    ENDPOINT_NOT_FOUND(HttpStatus.NOT_FOUND, "C004", "요청한 경로를 찾을 수 없습니다."),
    METHOD_NOT_ALLOWED(HttpStatus.METHOD_NOT_ALLOWED, "C005", "허용되지 않은 HTTP 메서드입니다."),
    UNSUPPORTED_MEDIA_TYPE(HttpStatus.UNSUPPORTED_MEDIA_TYPE, "C006", "지원하지 않는 Content-Type 입니다."),
    INTERNAL_SERVER_ERROR(HttpStatus.INTERNAL_SERVER_ERROR, "C999", "서버 오류가 발생했습니다."),

    // Auth
    UNAUTHORIZED(HttpStatus.UNAUTHORIZED, "A001", "인증이 필요합니다."),
    FORBIDDEN(HttpStatus.FORBIDDEN, "A002", "접근 권한이 없습니다."),
    INVALID_TOKEN(HttpStatus.UNAUTHORIZED, "A003", "유효하지 않은 토큰입니다."),
    EXPIRED_TOKEN(HttpStatus.UNAUTHORIZED, "A004", "만료된 토큰입니다."),
    INVALID_REFRESH_TOKEN(HttpStatus.UNAUTHORIZED, "A005", "유효하지 않은 리프레시 토큰입니다."),
    UNSUPPORTED_AUTH_PROVIDER(HttpStatus.BAD_REQUEST, "A006", "지원하지 않는 로그인 제공자입니다."),
    OAUTH_PROVIDER_ERROR(HttpStatus.INTERNAL_SERVER_ERROR, "A007", "소셜 로그인 처리 중 오류가 발생했습니다."),
    // 인계 코드가 없거나·만료됐거나·이미 쓰였거나·verifier 가 맞지 않는 경우를 한 코드로 묶는다.
    // 넷 다 클라이언트가 할 일은 "다시 로그인"이고, 나누면 코드 추측 공격에 힌트를 준다.
    INVALID_HANDOFF_CODE(HttpStatus.BAD_REQUEST, "A008",
            "로그인 인계 코드가 유효하지 않습니다. 다시 로그인해주세요."),

    // User
    USER_NOT_FOUND(HttpStatus.NOT_FOUND, "U001", "사용자를 찾을 수 없습니다.");

    private final HttpStatus status;
    private final String code;
    private final String message;
}

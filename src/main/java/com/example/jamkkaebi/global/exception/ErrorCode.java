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
    USER_NOT_FOUND(HttpStatus.NOT_FOUND, "U001", "사용자를 찾을 수 없습니다."),

    // Box — 일일 상자
    // "열 수 없다"를 한 코드로 묶는다. 무료를 이미 썼는지 선물권이 없는지는 클라이언트가 상자 상태
    // 응답에서 이미 알고 있고, 둘 다 할 일은 "기다리기"로 같다.
    BOX_NOT_READY(HttpStatus.CONFLICT, "B001", "지금은 상자를 열 수 없습니다."),
    // 작업대 만석은 따로 나눈다 — 유일하게 사용자가 지금 당장 풀 수 있는 원인이라(정화하러 가기)
    // 버튼 문구와 이동 경로가 달라진다.
    WORKBENCH_FULL(HttpStatus.CONFLICT, "B002",
            "작업대가 가득 찼습니다. 진행 중인 유물을 먼저 정화해주세요."),
    NOT_ENOUGH_JEONGSEONG(HttpStatus.CONFLICT, "B003", "정성이 부족합니다."),
    BOX_PURCHASE_LIMIT_REACHED(HttpStatus.CONFLICT, "B004", "오늘은 더 이상 상자를 구매할 수 없습니다."),

    // Workbench — 작업대 · 도깨비 성장
    ARTIFACT_NOT_OWNED(HttpStatus.NOT_FOUND, "W001", "보유하지 않은 유물입니다."),
    ARTIFACT_NOT_COMPLETED(HttpStatus.CONFLICT, "W002", "정령 수호까지 마친 유물만 할 수 있습니다."),
    GROWTH_GAUGE_NOT_FULL(HttpStatus.CONFLICT, "W003", "성장 게이지가 아직 가득 차지 않았습니다."),
    AWAKENING_STAGE_MISMATCH(HttpStatus.CONFLICT, "W004",
            "도깨비의 각성 단계가 바뀌었습니다. 새로고침 후 다시 시도해주세요."),

    // Purification — 유물 정화
    DIFFICULTY_LOCKED(HttpStatus.CONFLICT, "P001", "아직 열리지 않은 난이도입니다."),
    // 없는 토큰·남의 토큰·만료·무효화를 한 코드로 묶는다. 나누면 남의 세션 토큰을 찍어 볼 수 있다.
    PURIFICATION_SESSION_NOT_FOUND(HttpStatus.NOT_FOUND, "P002", "정화 세션이 없거나 만료되었습니다."),
    PURIFICATION_RESULT_REJECTED(HttpStatus.BAD_REQUEST, "P003", "정화 결과를 확인할 수 없습니다."),

    // Friend
    // 없는 코드와 차단 관계를 한 코드로 묶는다. 나누면 상대가 나를 차단했는지 역추적할 수 있다.
    FRIEND_CODE_NOT_FOUND(HttpStatus.NOT_FOUND, "F001", "존재하지 않는 코드입니다."),
    FRIEND_CODE_LOOKUP_LIMITED(HttpStatus.TOO_MANY_REQUESTS, "F002",
            "조회 시도가 너무 많습니다. 잠시 후 다시 시도해주세요."),
    SENT_REQUEST_LIMIT_REACHED(HttpStatus.CONFLICT, "F003", "보낸 요청이 가득 찼습니다."),
    RECEIVER_INBOX_FULL(HttpStatus.CONFLICT, "F004", "상대방의 수신함이 가득 찼습니다."),
    FRIEND_LIMIT_REACHED(HttpStatus.CONFLICT, "F005", "친구 목록이 가득 찼습니다."),
    TARGET_FRIEND_LIMIT_REACHED(HttpStatus.CONFLICT, "F006", "상대방의 친구 목록이 가득 찼습니다."),
    FRIEND_REQUEST_NOT_FOUND(HttpStatus.NOT_FOUND, "F007", "친구 요청을 찾을 수 없습니다."),
    FRIEND_NOT_FOUND(HttpStatus.NOT_FOUND, "F008", "친구를 찾을 수 없습니다."),
    GIFT_ALREADY_SENT_TODAY(HttpStatus.CONFLICT, "F009", "오늘은 이미 상자를 선물했습니다."),
    GIFT_RECEIVER_FULL(HttpStatus.CONFLICT, "F010", "상대방이 오늘 받을 수 있는 선물이 가득 찼습니다."),
    ALREADY_FRIENDS(HttpStatus.CONFLICT, "F011", "이미 친구입니다.");

    private final HttpStatus status;
    private final String code;
    private final String message;
}

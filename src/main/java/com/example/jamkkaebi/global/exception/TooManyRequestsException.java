package com.example.jamkkaebi.global.exception;

import lombok.Getter;

/**
 * 호출 한도를 넘은 요청. 언제 다시 시도할 수 있는지를 함께 담는다.
 *
 * <p>{@link GlobalExceptionHandler} 가 이 값을 {@code Retry-After} 헤더로 내보낸다 — 클라이언트가
 * 임의 간격으로 재시도하면 한도가 풀리기 전에 또 막히기 때문이다.
 */
@Getter
public class TooManyRequestsException extends BusinessException {

    private final long retryAfterSeconds;

    public TooManyRequestsException(ErrorCode errorCode, long retryAfterSeconds) {
        super(errorCode);
        this.retryAfterSeconds = Math.max(1L, retryAfterSeconds);
    }
}

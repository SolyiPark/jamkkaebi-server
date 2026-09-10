package com.example.jamkkaebi.global.response;

import com.example.jamkkaebi.global.exception.ErrorCode;
import com.fasterxml.jackson.annotation.JsonInclude;
import lombok.Getter;

import java.time.LocalDateTime;
import java.time.ZoneId;
import java.util.List;

/**
 * 모든 JSON 응답의 공통 래퍼.
 *
 * <p>클라이언트(게임)는 {@code success} 와 {@code code} 만 보고 분기한다. {@code data}(성공 시)와
 * {@code errors}(필드 단위 사유가 있을 때)는 {@code null} 이면 직렬화에서 빠진다.
 */
@Getter
@JsonInclude(JsonInclude.Include.NON_NULL)
public class ApiResponse<T> {

    private static final ZoneId KST = ZoneId.of("Asia/Seoul");

    private final boolean success;
    private final String code;
    private final String message;
    private final T data;                  // 성공 시
    private final List<FieldError> errors; // 실패 시
    private final LocalDateTime timestamp;

    private ApiResponse(boolean success, String code, String message,
                        T data, List<FieldError> errors) {
        this.success = success;
        this.code = code;
        this.message = message;
        this.data = data;
        this.errors = errors;
        this.timestamp = LocalDateTime.now(KST);
    }

    public static <T> ApiResponse<T> success(String code, String message, T data) {
        return new ApiResponse<>(true, code, message, data, null);
    }

    public static ApiResponse<Void> error(ErrorCode errorCode, List<FieldError> errors) {
        return new ApiResponse<>(false, errorCode.getCode(), errorCode.getMessage(), null, errors);
    }

    public static ApiResponse<Void> error(ErrorCode errorCode) {
        return error(errorCode, null);
    }
}

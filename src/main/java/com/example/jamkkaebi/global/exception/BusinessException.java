package com.example.jamkkaebi.global.exception;

import com.example.jamkkaebi.global.response.FieldError;
import lombok.Getter;

import java.util.List;

/**
 * 서비스 로직에서 발생한 비즈니스 오류를 표현하는 공통 예외.
 *
 * <p>발생한 {@link ErrorCode} 를 보관하고, {@link GlobalExceptionHandler} 가 이를 공통 오류 응답으로
 * 변환한다.
 */
@Getter
public class BusinessException extends RuntimeException {

    private final ErrorCode errorCode;
    private final List<FieldError> errors;

    public BusinessException(ErrorCode errorCode) {
        this(errorCode, null);
    }

    public BusinessException(ErrorCode errorCode, List<FieldError> errors) {
        super(errorCode.getMessage());
        this.errorCode = errorCode;
        this.errors = errors == null || errors.isEmpty() ? null : List.copyOf(errors);
    }
}

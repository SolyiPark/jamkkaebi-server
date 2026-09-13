package com.example.jamkkaebi.global.exception;

import com.example.jamkkaebi.global.response.ApiResponse;
import com.example.jamkkaebi.global.response.FieldError;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.HttpMediaTypeNotSupportedException;
import org.springframework.web.HttpRequestMethodNotSupportedException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.MissingServletRequestParameterException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.method.annotation.MethodArgumentTypeMismatchException;
import org.springframework.web.servlet.resource.NoResourceFoundException;

import java.util.List;

/**
 * 모든 REST Controller 에서 발생하는 예외를 공통 형식으로 처리한다.
 *
 * <p>컨트롤러마다 예외 처리를 반복하지 않도록 한곳에 모으고, 실패 응답을 {@link ApiResponse}
 * 형식으로 통일한다.
 */
@RestControllerAdvice
public class GlobalExceptionHandler {

    private static final Logger log = LoggerFactory.getLogger(GlobalExceptionHandler.class);
    private static final String INVALID_FORMAT_REASON = "올바른 형식의 값이어야 합니다.";
    private static final String REQUIRED_VALUE_REASON = "필수 값입니다.";

    @ExceptionHandler(BusinessException.class)
    public ResponseEntity<ApiResponse<Void>> handleBusinessException(BusinessException exception) {
        ErrorCode errorCode = exception.getErrorCode();
        return ResponseEntity.status(errorCode.getStatus())
                .body(ApiResponse.error(errorCode, exception.getErrors()));
    }

    /** 호출 한도 초과. 다시 시도할 수 있는 시점을 {@code Retry-After} 헤더(초)로 알려 준다. */
    @ExceptionHandler(TooManyRequestsException.class)
    public ResponseEntity<ApiResponse<Void>> handleTooManyRequests(TooManyRequestsException exception) {
        ErrorCode errorCode = exception.getErrorCode();
        return ResponseEntity.status(errorCode.getStatus())
                .header(org.springframework.http.HttpHeaders.RETRY_AFTER,
                        String.valueOf(exception.getRetryAfterSeconds()))
                .body(ApiResponse.error(errorCode));
    }

    /** 요청 본문 검증 실패. 필드별 사유를 errors 로 내려준다. */
    @ExceptionHandler(MethodArgumentNotValidException.class)
    public ResponseEntity<ApiResponse<Void>> handleMethodArgumentNotValid(
            MethodArgumentNotValidException exception) {
        List<FieldError> errors = exception.getBindingResult().getFieldErrors().stream()
                .map(fieldError -> new FieldError(fieldError.getField(), fieldError.getDefaultMessage()))
                .toList();
        return ResponseEntity.status(ErrorCode.INVALID_INPUT.getStatus())
                .body(ApiResponse.error(ErrorCode.INVALID_INPUT, errors));
    }

    @ExceptionHandler(MethodArgumentTypeMismatchException.class)
    public ResponseEntity<ApiResponse<Void>> handleTypeMismatch(
            MethodArgumentTypeMismatchException exception) {
        return invalidInput(new FieldError(exception.getName(), INVALID_FORMAT_REASON));
    }

    @ExceptionHandler(MissingServletRequestParameterException.class)
    public ResponseEntity<ApiResponse<Void>> handleMissingParameter(
            MissingServletRequestParameterException exception) {
        return invalidInput(new FieldError(exception.getParameterName(), REQUIRED_VALUE_REASON));
    }

    /** 본문이 없거나 JSON 이 깨진 요청. 알 수 없는 provider 값 같은 역직렬화 실패도 여기로 온다. */
    @ExceptionHandler(HttpMessageNotReadableException.class)
    public ResponseEntity<ApiResponse<Void>> handleNotReadable(HttpMessageNotReadableException exception) {
        return status(ErrorCode.INVALID_INPUT);
    }

    @ExceptionHandler(NoResourceFoundException.class)
    public ResponseEntity<ApiResponse<Void>> handleNoResourceFound(NoResourceFoundException exception) {
        return status(ErrorCode.ENDPOINT_NOT_FOUND);
    }

    @ExceptionHandler(HttpRequestMethodNotSupportedException.class)
    public ResponseEntity<ApiResponse<Void>> handleMethodNotSupported(
            HttpRequestMethodNotSupportedException exception) {
        return status(ErrorCode.METHOD_NOT_ALLOWED);
    }

    @ExceptionHandler(HttpMediaTypeNotSupportedException.class)
    public ResponseEntity<ApiResponse<Void>> handleMediaTypeNotSupported(
            HttpMediaTypeNotSupportedException exception) {
        return status(ErrorCode.UNSUPPORTED_MEDIA_TYPE);
    }

    /**
     * 서비스가 개별 코드로 변환하지 못한 무결성 위반. 원인은 로그로 남기고 클라이언트에는 충돌만
     * 알린다 — 제약 이름이나 SQL 이 그대로 나가면 스키마가 노출된다.
     */
    @ExceptionHandler(DataIntegrityViolationException.class)
    public ResponseEntity<ApiResponse<Void>> handleDataIntegrityViolation(
            DataIntegrityViolationException exception) {
        log.warn("데이터 무결성 제약을 위반했습니다.", exception);
        return status(ErrorCode.CONFLICT);
    }

    /** 예상하지 못한 예외. 스택 트레이스는 로그에만 남긴다. */
    @ExceptionHandler(Exception.class)
    public ResponseEntity<ApiResponse<Void>> handleException(Exception exception) {
        log.error("처리하지 못한 예외가 발생했습니다.", exception);
        return status(ErrorCode.INTERNAL_SERVER_ERROR);
    }

    private ResponseEntity<ApiResponse<Void>> invalidInput(FieldError error) {
        return ResponseEntity.status(ErrorCode.INVALID_INPUT.getStatus())
                .body(ApiResponse.error(ErrorCode.INVALID_INPUT, List.of(error)));
    }

    private ResponseEntity<ApiResponse<Void>> status(ErrorCode errorCode) {
        return ResponseEntity.status(errorCode.getStatus()).body(ApiResponse.error(errorCode));
    }
}

package com.example.jamkkaebi.global.response;

import lombok.Getter;

/** 검증 실패 등 필드 단위 사유. */
@Getter
public class FieldError {

    private final String field;
    private final String reason;

    public FieldError(String field, String reason) {
        this.field = field;
        this.reason = reason;
    }
}

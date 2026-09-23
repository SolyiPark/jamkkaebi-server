package com.example.jamkkaebi.box.dto.request;

import com.example.jamkkaebi.box.domain.BoxSource;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Pattern;

/**
 * 상자 열기 요청.
 *
 * @param source    여는 경로.
 * @param requestId 탭 1회마다 새로 만드는 멱등 키. 응답을 못 받아 재시도할 때는 <b>같은 값</b>을
 *                  보낸다.
 */
public record BoxOpenRequest(

        @NotNull(message = "필수 값입니다.")
        BoxSource source,

        @NotNull(message = "필수 값입니다.")
        @Pattern(
                regexp = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
                message = "UUID 형식이어야 합니다.")
        String requestId
) {
}

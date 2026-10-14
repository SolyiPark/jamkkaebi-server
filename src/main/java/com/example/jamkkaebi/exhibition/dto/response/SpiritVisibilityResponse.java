package com.example.jamkkaebi.exhibition.dto.response;

/**
 * 숨기기 / 보이기를 반영한 뒤의 값. 전체가 아니라 그 대상만 수정한다.
 */
public record SpiritVisibilityResponse(Integer relicId, boolean hidden) {
}

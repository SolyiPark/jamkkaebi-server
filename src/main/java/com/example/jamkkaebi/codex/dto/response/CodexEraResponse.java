package com.example.jamkkaebi.codex.dto.response;

import com.example.jamkkaebi.relic.domain.Era;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.util.List;

/**
 * 한 시대의 유물 카드와 건물 슬롯.
 *
 * @param collectionRate 이 시대 수집률(%) = 완료 수 ÷ 시대의 유물 수 (내림)
 * @param cards          {@code relicId} 오름차순
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record CodexEraResponse(
        Era era,
        int collectionRate,
        List<CodexCardView> cards,
        CodexBuildingView building
) {
}

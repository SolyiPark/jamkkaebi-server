package com.example.jamkkaebi.box.dto.response;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.box.domain.BoxResult;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 상자 하나를 연 결과.
 *
 * <p>신규 카드는 공개 규칙에 따라 <b>실루엣으로만</b> 내려간다.
 *
 * @param duplicateReward 중복 환전 결과. 신규면 {@code null}
 * @param spent           소비한 재화. 정성 구매가 아니면 {@code null}
 * @param box             연 뒤의 상자 상태.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record BoxOpenResponse(
        BoxResult result,
        ArtifactCard artifact,
        DuplicateReward duplicateReward,
        Spent spent,
        BoxStatusResponse box
) {

    // 중복 카드는 쌓이지 않고 즉시 재화가 된다.
    public record DuplicateReward(Era era, int eraCrystal, int jeongseong) {
    }

    public record Spent(int jeongseong) {
    }
}

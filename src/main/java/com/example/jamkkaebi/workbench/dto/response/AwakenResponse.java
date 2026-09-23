package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.artifact.dto.response.GaugeView;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.util.List;

/**
 * 강화 확정 결과.
 *
 * @param growthGauge          다음 단계용 게이지. 이월분이 들어 있다. 완전각성이면 {@code null}
 * @param carriedOver          이번 확정에서 다음 단계로 이월된 양. 없으면 {@code 0}
 * @param unlockedDifficulties 이번 확정으로 새로 열린 난이도. 없으면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record AwakenResponse(
        AwakeningStage awakeningStage,
        GaugeView growthGauge,
        Integer carriedOver,
        List<Difficulty> unlockedDifficulties,
        List<String> badgesEarned
) {
}

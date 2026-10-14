package com.example.jamkkaebi.purification.dto.response;

import com.example.jamkkaebi.purification.domain.NextAction;
import com.example.jamkkaebi.purification.domain.PlayResult;
import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.RelicStatus;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.dto.response.BuildingView;
import com.example.jamkkaebi.relic.dto.response.GaugeView;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.util.List;

/**
 * 한 판의 정산 결과.
 *
 * @param perfectStreak    이 도깨비의 연속 완벽 정화 수. 최초 정화면 {@code null}
 * @param revealed         이번 판에서 <b>새로</b> 도달한 공개 단계
 * @param lastClearedPhase 갱신된 복구 진행도
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record PurificationResultResponse(
        PlayResult result,
        boolean perfect,
        Integer perfectStreak,
        Rewards rewards,
        RelicCard relic,
        RelicStatus status,
        RevealLevel revealed,
        NextStep next,
        Phase lastClearedPhase,
        AwakeningView awakening,
        Clarity clarity,
        BuildingView buildingAwarded,
        List<String> badgesEarned
) {

    /**
     * 이번 판에서 얻은 것
     *
     * @param eraCrystal 보너스 타일로 캐낸 시대의 결정. 없으면 {@code null}
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record Rewards(int jeongseong, EraCrystalReward eraCrystal, int growthGauge) {
    }

    public record EraCrystalReward(Era era, int amount) {
    }

    /**
     * 결과 화면의 주 버튼.
     *
     * @param phase           이어서 할 페이즈. 끝났거나 실패면 {@code null}
     * @param remainingPhases 남은 복구 순서. 끝났거나 실패면 {@code null}
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record NextStep(NextAction action, Phase phase, List<Phase> remainingPhases) {

        public static NextStep of(NextAction action) {
            return new NextStep(action, null, null);
        }
    }

    /**
     * 각성 현황. 정령 수호를 마친 유물에만 있다.
     *
     * @param growthGauge 완전각성이면 {@code null}
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record AwakeningView(AwakeningStage stage, GaugeView growthGauge, boolean growthReady) {
    }
}

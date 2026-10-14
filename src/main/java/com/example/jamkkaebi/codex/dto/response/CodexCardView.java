package com.example.jamkkaebi.codex.dto.response;

import com.example.jamkkaebi.codex.domain.CodexCardState;
import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 시대 화면의 카드 한 장.
 *
 * @param relicId     슬롯 위치용. 항상 싣는다
 * @param relic       유물 카드. {@code LOCKED} 면 생략
 * @param awakeningStage 각성 단계 배지. {@code FULL_COLOR} 만
 * @param clarity        선명도 배지. {@code FULL_COLOR} 만
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record CodexCardView(
        Integer relicId,
        CodexCardState cardState,
        RelicCard relic,
        AwakeningStage awakeningStage,
        Clarity clarity
) {

    /**
     * @param owned   이 사용자의 진행 상태. 보유하지 않았으면 {@code null}
     * @param clarity 현재 선명도. 완료하지 않았으면 {@code null}
     */
    public static CodexCardView of(RelicMaster master, UserRelic owned, Clarity clarity) {
        CodexCardState state = CodexCardState.of(owned);
        if (state == CodexCardState.LOCKED) {
            return new CodexCardView(master.getId(), state, null, null, null);
        }
        boolean completed = state == CodexCardState.FULL_COLOR;
        return new CodexCardView(master.getId(), state, RelicCard.of(master, owned),
                completed ? owned.getAwakeningStage() : null,
                completed ? clarity : null);
    }
}

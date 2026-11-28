package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDateTime;

/**
 * 작업대의 도깨비 한 마리. 오염 관리까지 마쳐 이름이 전부 공개된 상태다.
 *
 * @param careCount        관리 플레이 성공 횟수
 * @param clarity          지금 선명도
 * @param lastPurifiedAt   마지막으로 정화에 성공한 시각
 * @param clarityChangesAt 이대로 두면 선명도가 한 칸 떨어지는 시각. 이미 재봉인됐으면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record SpiritView(
        Integer relicId,
        Era era,
        RevealLevel revealLevel,
        String poeticAlias,
        String realName,
        String spiritName,
        int careCount,
        Clarity clarity,
        LocalDateTime lastPurifiedAt,
        LocalDateTime clarityChangesAt
) {

    public static SpiritView of(RelicMaster master, UserRelic owned, Clarity clarity,
                                LocalDateTime clarityChangesAt) {
        RelicCard card = RelicCard.of(master, owned);
        return new SpiritView(card.relicId(), card.era(), card.revealLevel(),
                card.poeticAlias(), card.realName(), card.spiritName(),
                owned.getCareCount(), clarity, owned.getLastPurifiedAt(), clarityChangesAt);
    }
}

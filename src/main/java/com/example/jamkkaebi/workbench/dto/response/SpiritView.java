package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 작업대의 도깨비 한 마리. 오염 관리까지 마쳐 이름이 전부 공개된 상태다.
 *
 * @param careCount 관리 플레이 성공 횟수
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
        Clarity clarity
) {

    public static SpiritView of(RelicMaster master, UserRelic owned, Clarity clarity) {
        RelicCard card = RelicCard.of(master, owned);
        return new SpiritView(card.relicId(), card.era(), card.revealLevel(),
                card.poeticAlias(), card.realName(), card.spiritName(),
                owned.getCareCount(), clarity);
    }
}

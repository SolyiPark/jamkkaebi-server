package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.GaugeView;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 작업대의 도깨비 한 마리. 정령 수호까지 마쳐 이름이 전부 공개된 상태다.
 *
 * @param growthGauge 완전각성이면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record SpiritView(
        Integer relicId,
        Era era,
        RevealLevel revealLevel,
        String poeticAlias,
        String realName,
        String spiritName,
        AwakeningStage awakeningStage,
        boolean growthReady,
        GaugeView growthGauge,
        Clarity clarity
) {

    public static SpiritView of(RelicMaster master, UserRelic owned,
                                boolean growthReady, GaugeView growthGauge, Clarity clarity) {
        RelicCard card = RelicCard.of(master, owned);
        return new SpiritView(card.relicId(), card.era(), card.revealLevel(),
                card.poeticAlias(), card.realName(), card.spiritName(),
                owned.getAwakeningStage(), growthReady, growthGauge, clarity);
    }
}

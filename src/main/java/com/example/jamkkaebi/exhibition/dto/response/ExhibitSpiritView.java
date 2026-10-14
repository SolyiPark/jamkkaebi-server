package com.example.jamkkaebi.exhibition.dto.response;

import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 내 전시관의 도깨비 한 마리.
 *
 * <p>친구에게 보이는 모습({@link ExhibitionView.SpiritView})과 달리 <b>선명도·배치 여부·숨김</b>이 실린다.
 * (선명도는 방치 상태를 비교하지 않도록 친구에게는 내보내지 않는다. )
 *
 * @param clarity  밝기 연출. {@code RESEALED} 면 실루엣으로 그린다
 * @param position 배치 좌표. 배치하지 않았으면 생략
 */

@JsonInclude(JsonInclude.Include.NON_NULL)
public record ExhibitSpiritView(
        Integer relicId,
        Era era,
        RevealLevel revealLevel,
        String poeticAlias,
        String realName,
        String spiritName,
        AwakeningStage awakeningStage,
        Clarity clarity,
        boolean displayed,
        boolean hidden,
        ExhibitionView.Position position
) {

    public static ExhibitSpiritView of(RelicMaster master, UserRelic owned, Clarity clarity) {
        RelicCard card = RelicCard.of(master, owned);
        return new ExhibitSpiritView(card.relicId(), card.era(), card.revealLevel(),
                card.poeticAlias(), card.realName(), card.spiritName(),
                owned.getAwakeningStage(), clarity,
                owned.isDisplayed(), owned.isHidden(),
                owned.isDisplayed() ? new ExhibitionView.Position(owned.getPosX(), owned.getPosY()) : null);
    }
}

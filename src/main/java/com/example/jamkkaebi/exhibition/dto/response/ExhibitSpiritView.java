package com.example.jamkkaebi.exhibition.dto.response;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.RevealLevel;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
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
        Integer artifactId,
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

    public static ExhibitSpiritView of(ArtifactMaster master, UserArtifact owned, Clarity clarity) {
        ArtifactCard card = ArtifactCard.of(master, owned);
        return new ExhibitSpiritView(card.artifactId(), card.era(), card.revealLevel(),
                card.poeticAlias(), card.realName(), card.spiritName(),
                owned.getAwakeningStage(), clarity,
                owned.isDisplayed(), owned.isHidden(),
                owned.isDisplayed() ? new ExhibitionView.Position(owned.getPosX(), owned.getPosY()) : null);
    }
}

package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.Phase;
import com.example.jamkkaebi.artifact.domain.RevealLevel;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 작업대의 미확인 유물 한 칸. 유물 카드에 "다음에 할 페이즈"를 얹은 모양이다.
 *
 * <p>카드 필드를 중첩하지 않고 펼쳐 담는다
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record SlotView(
        Integer artifactId,
        Era era,
        RevealLevel revealLevel,
        String poeticAlias,
        String realName,
        String spiritName,
        Phase currentPhase
) {

    public static SlotView of(ArtifactMaster master, UserArtifact owned) {
        ArtifactCard card = ArtifactCard.of(master, owned);
        return new SlotView(card.artifactId(), card.era(), card.revealLevel(),
                card.poeticAlias(), card.realName(), card.spiritName(), owned.getCurrentPhase());
    }
}

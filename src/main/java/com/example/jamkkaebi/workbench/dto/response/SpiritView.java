package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.RevealLevel;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.artifact.dto.response.GaugeView;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 작업대의 도깨비 한 마리. 정령 수호까지 마쳐 이름이 전부 공개된 상태다.
 *
 * @param growthGauge 완전각성이면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record SpiritView(
        Integer artifactId,
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

    public static SpiritView of(ArtifactMaster master, UserArtifact owned,
                                boolean growthReady, GaugeView growthGauge, Clarity clarity) {
        ArtifactCard card = ArtifactCard.of(master, owned);
        return new SpiritView(card.artifactId(), card.era(), card.revealLevel(),
                card.poeticAlias(), card.realName(), card.spiritName(),
                owned.getAwakeningStage(), growthReady, growthGauge, clarity);
    }
}

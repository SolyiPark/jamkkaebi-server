package com.example.jamkkaebi.artifact.dto.response;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.RevealLevel;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 유물 한 개.
 *
 * <p><b>정체 공개는 서버가 담당한다.</b> 도달하지 않은 단계의 이름은 {@code null} 로 두고 직렬화에서
 * 빠진다 — 클라이언트가 화면에서 가려도 패킷에 이름이 들어 있으면 단계적 공개가 깨진다.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record ArtifactCard(
        Integer artifactId,
        Era era,
        RevealLevel revealLevel,
        String poeticAlias,
        String realName,
        String spiritName
) {

    /**
     * 진행도에 맞춰 공개할 만큼만 담은 카드를 만든다.
     *
     * @param master 유물 마스터
     * @param owned  이 사용자의 진행 상태
     */
    public static ArtifactCard of(ArtifactMaster master, UserArtifact owned) {
        RevealLevel revealLevel = owned.revealLevel();
        return new ArtifactCard(
                master.getId(),
                master.getEra(),
                revealLevel,
                revealLevel.showsAlias() ? master.getPoeticAlias() : null,
                revealLevel.showsRealName() ? master.getRealName() : null,
                revealLevel.showsRealName() ? spiritNameOf(master, owned) : null);
    }

    // 사용자가 지어준 이름이 있으면 그것, 없으면 기본 이름
    public static String spiritNameOf(ArtifactMaster master, UserArtifact owned) {
        String customName = owned.getCustomName();
        return customName == null || customName.isBlank() ? master.getSpiritDefaultName() : customName;
    }
}

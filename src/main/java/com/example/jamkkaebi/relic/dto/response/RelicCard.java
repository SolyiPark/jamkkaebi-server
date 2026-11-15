package com.example.jamkkaebi.relic.dto.response;

import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 유물 한 개.
 *
 * <p><b>정체 공개는 서버가 담당한다.</b> 도달하지 않은 단계의 이름은 {@code null}.
 * 시적 별칭은 항상, 도깨비 이름은 완료 후에만 담는다.
 */

@JsonInclude(JsonInclude.Include.NON_NULL)
public record RelicCard(
        Integer relicId,
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
    public static RelicCard of(RelicMaster master, UserRelic owned) {
        RevealLevel revealLevel = owned.revealLevel();
        return new RelicCard(
                master.getId(),
                master.getEra(),
                revealLevel,
                master.getPoeticAlias(),
                revealLevel.showsRealName() ? master.getRealName() : null,
                // 도깨비 이름은 최초 정화를 끝까지 마친 뒤에만 공개한다.
                owned.isCompleted() ? spiritNameOf(master, owned) : null);
    }

    // 사용자가 지어준 이름 (없으면 기본 이름)
    public static String spiritNameOf(RelicMaster master, UserRelic owned) {
        String customName = owned.getCustomName();
        return customName == null || customName.isBlank() ? master.getSpiritDefaultName() : customName;
    }
}

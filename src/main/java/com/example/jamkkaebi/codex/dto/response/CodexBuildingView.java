package com.example.jamkkaebi.codex.dto.response;

import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 시대 화면의 건물 슬롯 한 칸. 건물은 시대 유물 3종을 마치면 자동으로 지급되므로 도감에서는 보유 여부만
 * 보여 준다.
 *
 * @param name  건물 이름.
 * @param level 건물 레벨.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record CodexBuildingView(Integer buildingId, boolean acquired, String name, Integer level) {

    /**
     * @param owned 이 사용자가 받은 건물. 받지 않았으면 {@code null}
     */
    public static CodexBuildingView of(BuildingMaster master, UserBuilding owned) {
        if (owned == null) {
            return new CodexBuildingView(master.getId(), false, null, null);
        }
        return new CodexBuildingView(master.getId(), true, master.getName(), owned.getLevel());
    }
}

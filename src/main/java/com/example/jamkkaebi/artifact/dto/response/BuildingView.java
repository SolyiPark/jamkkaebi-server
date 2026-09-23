package com.example.jamkkaebi.artifact.dto.response;

import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserBuilding;

/**
 * 보유한 건물 한 채. 건물은 플레이 대상이 아니라 시대의 결정으로 키우는 대상이라 진행도가 없고
 * 레벨만 있다.
 */
public record BuildingView(Integer buildingId, Era era, String name, int level, int maxLevel) {

    public static BuildingView of(BuildingMaster master, UserBuilding owned) {
        return new BuildingView(master.getId(), master.getEra(), master.getName(),
                owned.getLevel(), BuildingMaster.MAX_LEVEL);
    }
}

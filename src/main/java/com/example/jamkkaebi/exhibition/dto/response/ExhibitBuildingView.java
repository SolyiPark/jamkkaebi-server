package com.example.jamkkaebi.exhibition.dto.response;

import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 내 전시관의 건물 한 채. 건물은 숨기기 대상이 아니라 배치 여부만 있다.
 *
 * @param position 배치 좌표. 배치하지 않았으면 키 생략
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record ExhibitBuildingView(
        Integer buildingId,
        Era era,
        String name,
        int level,
        boolean displayed,
        ExhibitionView.Position position
) {

    public static ExhibitBuildingView of(BuildingMaster master, UserBuilding owned) {
        return new ExhibitBuildingView(master.getId(), master.getEra(), master.getName(), owned.getLevel(),
                owned.isDisplayed(),
                owned.isDisplayed() ? new ExhibitionView.Position(owned.getPosX(), owned.getPosY()) : null);
    }
}

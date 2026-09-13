package com.example.jamkkaebi.friend.spi;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.Era;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.annotation.JsonProperty;

import java.util.List;

/**
 * 친구에게 공개되는 전시관 한 장면. 스냅샷으로 저장되는 형태이자 친구 전시관 응답의 본문이다.
 *
 * @param grid      배치 좌표 범위
 * @param spirits   배치되고 숨기지 않은 정령
 * @param buildings 배치된 건물
 */
public record ExhibitionView(Grid grid, List<SpiritView> spirits, List<BuildingView> buildings) {

    public ExhibitionView {
        spirits = spirits == null ? List.of() : List.copyOf(spirits);
        buildings = buildings == null ? List.of() : List.copyOf(buildings);
    }

    public record Grid(int width, int height) {
    }

    public record Position(int x, int y) {
    }

    /**
     * 전시관의 정령. 친구 전시관에 나오는 정령은 전부 이름이 공개된(정령 수호 완료) 정령이다.
     *
     * @param spiritName 주인이 지어 준 이름. 짓지 않았으면 기본 이름
     */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    @JsonIgnoreProperties(ignoreUnknown = true)
    public record SpiritView(
            Integer artifactId,
            Era era,
            String poeticAlias,
            String realName,
            String spiritName,
            AwakeningStage awakeningStage,
            Position position
    ) {

        @JsonProperty("revealLevel")
        public String revealLevel() {
            return "NAMED";
        }
    }

    public record BuildingView(Integer buildingId, Era era, String name, int level, Position position) {
    }
}

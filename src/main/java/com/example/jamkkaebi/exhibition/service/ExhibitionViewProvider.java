package com.example.jamkkaebi.exhibition.service;

import com.example.jamkkaebi.exhibition.config.ExhibitionProperties;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.friend.spi.ExhibitionViewReader;
import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RelicStatus;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.example.jamkkaebi.relic.repository.UserBuildingRepository;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.RelicCatalog;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.Comparator;
import java.util.List;
import java.util.Map;

/**
 * 친구에게 보여 줄 전시관을 만든다. (친구 도메인의 {@link ExhibitionViewReader} 의 구현)
 *
 *
 * <p><b>내 전시관과 다른 점 — 배치한 것만 담고, 선명도는 싣지 않는다.
 */
@Service
public class ExhibitionViewProvider implements ExhibitionViewReader {

    private final UserRelicRepository userRelicRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final RelicCatalog catalog;
    private final ExhibitionProperties properties;

    public ExhibitionViewProvider(UserRelicRepository userRelicRepository,
                                  UserBuildingRepository userBuildingRepository,
                                  RelicCatalog catalog,
                                  ExhibitionProperties properties) {
        this.userRelicRepository = userRelicRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.properties = properties;
    }

    @Override
    @Transactional(readOnly = true)
    public ExhibitionView readPublicView(Long userId) {
        List<UserRelic> shown = userRelicRepository.findAllByUserIdAndStatus(userId, RelicStatus.COMPLETED)
                .stream()
                .filter(relic -> relic.isDisplayed() && !relic.isHidden())
                .sorted(Comparator.comparing(UserRelic::getRelicId))
                .toList();
        Map<Integer, RelicMaster> relicMasters = catalog.relics(
                shown.stream().map(UserRelic::getRelicId).toList());
        List<ExhibitionView.SpiritView> spirits = shown.stream()
                .map(relic -> spiritOf(relicMasters.get(relic.getRelicId()), relic))
                .toList();

        List<UserBuilding> placed = userBuildingRepository.findAllByUserId(userId).stream()
                .filter(UserBuilding::isDisplayed)
                .sorted(Comparator.comparing(UserBuilding::getBuildingId))
                .toList();
        Map<Integer, BuildingMaster> buildingMasters = catalog.buildings(
                placed.stream().map(UserBuilding::getBuildingId).toList());
        List<ExhibitionView.BuildingView> buildings = placed.stream()
                .map(building -> buildingOf(buildingMasters.get(building.getBuildingId()), building))
                .toList();

        return new ExhibitionView(properties.grid(), spirits, buildings);
    }

    //기본 빈 전시관
    @Override
    public ExhibitionView emptyView() {
        return new ExhibitionView(properties.grid(), List.of(), List.of());
    }

    private ExhibitionView.SpiritView spiritOf(RelicMaster master, UserRelic owned) {
        RelicCard card = RelicCard.of(master, owned);
        return new ExhibitionView.SpiritView(card.relicId(), card.era(), card.poeticAlias(), card.realName(),
                card.spiritName(), owned.getAwakeningStage(),
                new ExhibitionView.Position(owned.getPosX(), owned.getPosY()));
    }

    private ExhibitionView.BuildingView buildingOf(BuildingMaster master, UserBuilding owned) {
        return new ExhibitionView.BuildingView(master.getId(), master.getEra(), master.getName(), owned.getLevel(),
                new ExhibitionView.Position(owned.getPosX(), owned.getPosY()));
    }
}

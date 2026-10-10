package com.example.jamkkaebi.exhibition.service;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.ArtifactStatus;
import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.domain.UserBuilding;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.artifact.repository.UserBuildingRepository;
import com.example.jamkkaebi.artifact.service.ArtifactCatalog;
import com.example.jamkkaebi.exhibition.config.ExhibitionProperties;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.friend.spi.ExhibitionViewReader;
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

    private final UserArtifactRepository userArtifactRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final ArtifactCatalog catalog;
    private final ExhibitionProperties properties;

    public ExhibitionViewProvider(UserArtifactRepository userArtifactRepository,
                                  UserBuildingRepository userBuildingRepository,
                                  ArtifactCatalog catalog,
                                  ExhibitionProperties properties) {
        this.userArtifactRepository = userArtifactRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.properties = properties;
    }

    @Override
    @Transactional(readOnly = true)
    public ExhibitionView readPublicView(Long userId) {
        List<UserArtifact> shown = userArtifactRepository.findAllByUserIdAndStatus(userId, ArtifactStatus.COMPLETED)
                .stream()
                .filter(artifact -> artifact.isDisplayed() && !artifact.isHidden())
                .sorted(Comparator.comparing(UserArtifact::getArtifactId))
                .toList();
        Map<Integer, ArtifactMaster> artifactMasters = catalog.artifacts(
                shown.stream().map(UserArtifact::getArtifactId).toList());
        List<ExhibitionView.SpiritView> spirits = shown.stream()
                .map(artifact -> spiritOf(artifactMasters.get(artifact.getArtifactId()), artifact))
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

    private ExhibitionView.SpiritView spiritOf(ArtifactMaster master, UserArtifact owned) {
        ArtifactCard card = ArtifactCard.of(master, owned);
        return new ExhibitionView.SpiritView(card.artifactId(), card.era(), card.poeticAlias(), card.realName(),
                card.spiritName(), owned.getAwakeningStage(),
                new ExhibitionView.Position(owned.getPosX(), owned.getPosY()));
    }

    private ExhibitionView.BuildingView buildingOf(BuildingMaster master, UserBuilding owned) {
        return new ExhibitionView.BuildingView(master.getId(), master.getEra(), master.getName(), owned.getLevel(),
                new ExhibitionView.Position(owned.getPosX(), owned.getPosY()));
    }
}

package com.example.jamkkaebi.exhibition.service;

import com.example.jamkkaebi.exhibition.config.ExhibitionProperties;
import com.example.jamkkaebi.exhibition.domain.ExhibitItemType;
import com.example.jamkkaebi.exhibition.domain.ExhibitionChangedEvent;
import com.example.jamkkaebi.exhibition.dto.request.ExhibitionLayoutRequest;
import com.example.jamkkaebi.exhibition.dto.response.ExhibitBuildingView;
import com.example.jamkkaebi.exhibition.dto.response.ExhibitSpiritView;
import com.example.jamkkaebi.exhibition.dto.response.ExhibitionResponse;
import com.example.jamkkaebi.exhibition.dto.response.SpiritVisibilityResponse;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.repository.UserBuildingRepository;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.ClarityPolicy;
import com.example.jamkkaebi.relic.service.RelicCatalog;
import com.example.jamkkaebi.relic.service.RelicOwnership;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import com.example.jamkkaebi.user.service.UserProfileService;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * 내 전시관 — 조회, 배치 저장, 도깨비 숨기기.
 *
 * <p>배치 상태는 유물·건물 행({@code user_relics} · {@code user_buildings})에 좌표와 함께 들어 있다.
 * (따로 테이블로 두지 않는다.)
 */
@Service
public class ExhibitionService {

    private final UserProfileService userProfileService;
    private final UserRepository userRepository;
    private final UserRelicRepository userRelicRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final RelicCatalog catalog;
    private final RelicOwnership ownership;
    private final ClarityPolicy clarityPolicy;
    private final ExhibitionProperties properties;
    private final GameClock gameClock;
    private final ApplicationEventPublisher eventPublisher;

    public ExhibitionService(UserProfileService userProfileService,
                             UserRepository userRepository,
                             UserRelicRepository userRelicRepository,
                             UserBuildingRepository userBuildingRepository,
                             RelicCatalog catalog,
                             RelicOwnership ownership,
                             ClarityPolicy clarityPolicy,
                             ExhibitionProperties properties,
                             GameClock gameClock,
                             ApplicationEventPublisher eventPublisher) {
        this.userProfileService = userProfileService;
        this.userRepository = userRepository;
        this.userRelicRepository = userRelicRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.ownership = ownership;
        this.clarityPolicy = clarityPolicy;
        this.properties = properties;
        this.gameClock = gameClock;
        this.eventPublisher = eventPublisher;
    }

    // 홈 화면 — 둘 수 있는 도깨비·건물 전부와 배치 상태
    @Transactional(readOnly = true)
    public ExhibitionResponse exhibition(String friendCode) {
        User user = userProfileService.getByFriendCode(friendCode);
        return assemble(
                userRelicRepository.findAllByUserId(user.getId()),
                userBuildingRepository.findAllByUserId(user.getId()));
    }

    /**
     * 배치 저장. <b>전체 배치를 한 번에</b> 받아 목록에 없는 것은 전시관에서 내린다.
     *
     * <p>하나라도 둘 수 없으면 아무것도 바꾸지 않는다. 사용자 행을 먼저 잠가 같은 사용자의 저장 두 건이
     * 줄을 서므로 나중 저장을 따른다.
     */
    @Transactional
    public ExhibitionResponse saveLayout(String friendCode, List<ExhibitionLayoutRequest.Item> items) {
        validate(items);

        User user = userRepository.findByFriendCodeForUpdate(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
        List<UserRelic> relics = userRelicRepository.findAllByUserId(user.getId());
        List<UserBuilding> buildings = userBuildingRepository.findAllByUserId(user.getId());

        Map<Integer, ExhibitionLayoutRequest.Item> spiritPlacements = new HashMap<>();
        Map<Integer, ExhibitionLayoutRequest.Item> buildingPlacements = new HashMap<>();
        requirePlaceable(items, relics, buildings, spiritPlacements, buildingPlacements);

        for (UserRelic relic : relics) {
            ExhibitionLayoutRequest.Item placement = spiritPlacements.get(relic.getRelicId());
            if (placement != null) {
                relic.placeAt(placement.x(), placement.y());
            } else if (relic.isDisplayed()) {
                relic.removeFromExhibition();
            }
        }
        for (UserBuilding building : buildings) {
            ExhibitionLayoutRequest.Item placement = buildingPlacements.get(building.getBuildingId());
            if (placement != null) {
                building.placeAt(placement.x(), placement.y());
            } else if (building.isDisplayed()) {
                building.removeFromExhibition();
            }
        }

        eventPublisher.publishEvent(new ExhibitionChangedEvent(user.getId(), gameClock.now()));
        return assemble(relics, buildings);
    }

    /**
     * 도깨비 숨기기 / 보이기. 숨긴 도깨비는 친구 전시관에서 빠진다. 배치 좌표는 그대로 둔다.
     *
     * <p>배치 저장과 같이 <b>사용자 행을 첫 읽기로 잠근다.</b>
     */
    @Transactional
    public SpiritVisibilityResponse changeVisibility(String friendCode, Integer relicId, boolean hidden) {
        User user = userRepository.findByFriendCodeForUpdate(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
        UserRelic userRelic = ownership.requireCompleted(user.getId(), relicId).userRelic();

        if (userRelic.isHidden() != hidden) {
            userRelic.changeHidden(hidden);
            eventPublisher.publishEvent(new ExhibitionChangedEvent(user.getId(), gameClock.now()));
        }
        return new SpiritVisibilityResponse(relicId, hidden);
    }

    /**
     * 형식 검사
     *
     * <p>사유를 항목마다 모아 한 번에 돌려준다.
     */
    private void validate(List<ExhibitionLayoutRequest.Item> items) {
        List<FieldError> errors = new ArrayList<>();
        Set<Slot> seen = new HashSet<>();
        for (int i = 0; i < items.size(); i++) {
            ExhibitionLayoutRequest.Item item = items.get(i);
            //좌표가 그리드 안인지
            if (item.x() < 0 || item.x() >= properties.gridWidth()) {
                errors.add(new FieldError("items[%d].x".formatted(i),
                        "0 이상 %d 미만이어야 합니다.".formatted(properties.gridWidth())));
            }
            if (item.y() < 0 || item.y() >= properties.gridHeight()) {
                errors.add(new FieldError("items[%d].y".formatted(i),
                        "0 이상 %d 미만이어야 합니다.".formatted(properties.gridHeight())));
            }
            // 중복 검사
            if (!seen.add(new Slot(item.type(), item.id()))) {
                errors.add(new FieldError("items[%d]".formatted(i), "같은 항목이 두 번 들어 있습니다."));
            }
        }
        if (!errors.isEmpty()) {
            throw new BusinessException(ErrorCode.INVALID_INPUT, errors);
        }
    }

    //요청한 항목이 모두 둘 수 있는 것인지 확인하고, 번호별 배치로 나눠 담는다.
    private void requirePlaceable(List<ExhibitionLayoutRequest.Item> items,
                                  List<UserRelic> relics,
                                  List<UserBuilding> buildings,
                                  Map<Integer, ExhibitionLayoutRequest.Item> spiritPlacements,
                                  Map<Integer, ExhibitionLayoutRequest.Item> buildingPlacements) {
        Set<Integer> placeableSpirits = new HashSet<>();
        relics.stream().filter(UserRelic::isCompleted)
                .forEach(relic -> placeableSpirits.add(relic.getRelicId()));
        Set<Integer> ownedBuildings = new HashSet<>();
        buildings.forEach(building -> ownedBuildings.add(building.getBuildingId()));

        List<FieldError> errors = new ArrayList<>();
        for (int i = 0; i < items.size(); i++) {
            ExhibitionLayoutRequest.Item item = items.get(i);
            boolean placeable = item.type() == ExhibitItemType.SPIRIT
                    ? placeableSpirits.contains(item.id())
                    : ownedBuildings.contains(item.id());
            if (!placeable) {
                errors.add(new FieldError("items[%d]".formatted(i), item.type() == ExhibitItemType.SPIRIT
                        ? "정령 수호까지 마친 도깨비만 둘 수 있습니다."
                        : "보유한 건물만 둘 수 있습니다."));
                continue;
            }
            (item.type() == ExhibitItemType.SPIRIT ? spiritPlacements : buildingPlacements).put(item.id(), item);
        }
        if (!errors.isEmpty()) {
            throw new BusinessException(ErrorCode.EXHIBIT_ITEM_NOT_PLACEABLE, errors);
        }
    }

    private ExhibitionResponse assemble(List<UserRelic> relics, List<UserBuilding> buildings) {
        LocalDateTime now = gameClock.now();

        List<UserRelic> completed = relics.stream()
                .filter(UserRelic::isCompleted)
                .sorted(Comparator.comparing(UserRelic::getRelicId))
                .toList();
        Map<Integer, RelicMaster> relicMasters = catalog.relics(
                completed.stream().map(UserRelic::getRelicId).toList());
        List<ExhibitSpiritView> spirits = completed.stream()
                .map(relic -> ExhibitSpiritView.of(
                        relicMasters.get(relic.getRelicId()),
                        relic,
                        clarityPolicy.clarityOf(relic, now)))
                .toList();

        Map<Integer, BuildingMaster> buildingMasters = catalog.buildings(
                buildings.stream().map(UserBuilding::getBuildingId).toList());
        List<ExhibitBuildingView> buildingViews = buildings.stream()
                .sorted(Comparator.comparing(UserBuilding::getBuildingId))
                .map(building -> ExhibitBuildingView.of(buildingMasters.get(building.getBuildingId()), building))
                .toList();

        return new ExhibitionResponse(properties.grid(), spirits, buildingViews);
    }

    private record Slot(ExhibitItemType type, Integer id) {
    }
}
